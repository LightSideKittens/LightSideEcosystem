using System;
using System.Collections.Generic;
using System.IO;
using LightSide;
using LightSide.Benchmark;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Generates the animation comparison's content from <see cref="AnimationBenchmarkSpec"/>: for every case owning
/// content, a MoveIt, an Animator, a legacy Animation and a Playables prefab of one rig, the Mecanim and legacy clips
/// and the controller holding the same keys, the override layer's mask, the flipbook's sprites, the catalog the suite
/// reads, and the suite's scene when it is missing.
/// </summary>
internal static class AnimationBenchmarkContentBuilder
{
    const string Folder = "Assets/MoveIt_MySpace/AnimationBenchmark";
    const string ContentFolder = Folder + "/Content";
    const string CatalogPath = Folder + "/AnimationBenchmarkContent.asset";
    const string ScenePath = "Assets/MoveIt_MySpace/MoveItAnimationBenchmark.unity";
    const int SpriteSize = 32;

    [MenuItem(LightSideCore.Menu.ToolsRoot + "Benchmarks/Generate Animation Content")]
    static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Animation benchmark content is generated outside play mode.");

        if (AssetDatabase.IsValidFolder(ContentFolder)) AssetDatabase.DeleteAsset(ContentFolder);
        AssetDatabase.CreateFolder(Folder, "Content");
        var sprites = CreateSprites();
        var entries = new List<AnimationBenchmarkContent.Entry>();
        InScratchScene(() =>
        {
            foreach (var benchmarkCase in AnimationBenchmarkSpec.Cases)
                if (benchmarkCase.OwnsContent)
                    entries.Add(Build(benchmarkCase, sprites));
        });

        var catalog = AssetDatabase.LoadAssetAtPath<AnimationBenchmarkContent>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<AnimationBenchmarkContent>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.Assign(AnimationBenchmarkSpec.ContentVersion, entries.ToArray(), sprites);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        if (!File.Exists(ScenePath)) CreateScene(catalog);
        Debug.Log($"[AnimationBenchmark] Generated content version {AnimationBenchmarkSpec.ContentVersion} " +
                  $"for {entries.Count} cases in {ContentFolder}.");
    }

    /// <summary>
    /// Runs <paramref name="build"/> with an empty additive scene active, so created objects never enter an open
    /// scene, then closes it unsaved and restores the active scene.
    /// </summary>
    static void InScratchScene(Action build)
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            build();
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Sprite[] CreateSprites()
    {
        AssetDatabase.CreateFolder(ContentFolder, "Frames");
        var sprites = new Sprite[AnimationBenchmarkSpec.SpriteFrames];
        for (var frame = 0; frame < sprites.Length; frame++)
        {
            var path = $"{ContentFolder}/Frames/Frame{frame}.png";
            var color = (Color32)Color.HSVToRGB(frame / (float)sprites.Length, 0.6f, 0.95f);
            var pixels = new Color32[SpriteSize * SpriteSize];
            for (var y = 0; y < SpriteSize; y++)
                for (var x = 0; x < SpriteSize; x++)
                    pixels[y * SpriteSize + x] = y < (frame + 1) * SpriteSize / sprites.Length && x >= SpriteSize / 4 &&
                                                 x < SpriteSize * 3 / 4
                        ? new Color32(255, 255, 255, 255)
                        : color;
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            sprites[frame] = AssetDatabase.LoadAssetAtPath<Sprite>(path) ??
                             throw new InvalidOperationException($"'{path}' imported without a sprite.");
        }
        return sprites;
    }

    static AnimationBenchmarkContent.Entry Build(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites)
    {
        AssetDatabase.CreateFolder(ContentFolder, benchmarkCase.Id);
        var folder = $"{ContentFolder}/{benchmarkCase.Id}";
        var stem = $"{folder}/{benchmarkCase.Id}";
        var participants = AnimationBenchmarkParticipant.All();
        bool Supports(string key) => Array.Find(participants, participant => participant.Key == key)
            .Unsupported(benchmarkCase) == null;

        var clips = new AnimationClip[benchmarkCase.Clips.Length];
        for (var c = 0; c < clips.Length; c++)
            clips[c] = CreateClip(benchmarkCase, c, false, sprites, $"{stem}.{benchmarkCase.Clips[c].Name}.anim");
        var mask = benchmarkCase.LayerClip >= 0 ? CreateMask(benchmarkCase, $"{stem}.upper.mask") : null;
        var controller = CreateController(benchmarkCase, clips, mask, $"{stem}.controller");

        GameObject legacy = null;
        if (Supports("legacy"))
        {
            AssetDatabase.CreateFolder(folder, "Legacy");
            var legacyClips = new AnimationClip[benchmarkCase.Clips.Length];
            for (var c = 0; c < legacyClips.Length; c++)
                legacyClips[c] = CreateClip(benchmarkCase, c, true, sprites,
                    $"{folder}/Legacy/{benchmarkCase.Clips[c].Name}.anim");
            legacy = SavePrefab(BuildLegacy(benchmarkCase, sprites, legacyClips), $"{stem}.legacy.prefab");
        }

        return new AnimationBenchmarkContent.Entry
        {
            id = benchmarkCase.Id,
            moveit = Supports("moveit")
                ? SavePrefab(BuildMoveIt(benchmarkCase, sprites, " (MoveIt)"), $"{stem}.moveit.prefab")
                : null,
            moveitShared = Supports("moveit-shared")
                ? SaveShared(BuildMoveIt(benchmarkCase, sprites, " (MoveIt shared)"), $"{stem}.moveit-shared.prefab")
                : null,
            moveitCompressed = Supports("moveit-compressed")
                ? SaveCompressed(BuildMoveIt(benchmarkCase, sprites, " (MoveIt compressed)"),
                    $"{stem}.moveit-compressed.prefab")
                : null,
            animator = Supports("animator")
                ? SavePrefab(BuildAnimator(benchmarkCase, sprites, controller), $"{stem}.animator.prefab")
                : null,
            legacy = legacy,
            playables = Supports("playables")
                ? SavePrefab(BuildPlayables(benchmarkCase, sprites), $"{stem}.playables.prefab")
                : null,
            clips = clips,
            mask = mask,
        };
    }

    static GameObject BuildRig(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites, string name)
    {
        GameObject root;
        switch (benchmarkCase.Rig)
        {
            case AnimationBenchmarkRig.Skeleton:
                root = new GameObject(name);
                var bones = new Transform[benchmarkCase.Bones];
                bones[0] = root.transform;
                for (var bone = 1; bone < bones.Length; bone++)
                {
                    var child = new GameObject(AnimationBenchmarkSpec.BoneName(bone)).transform;
                    child.SetParent(bones[AnimationBenchmarkSpec.Parent(bone)], false);
                    child.localPosition = AnimationBenchmarkSpec.RestPosition(bone);
                    bones[bone] = child;
                }
                break;
            case AnimationBenchmarkRig.Image:
                root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ((RectTransform)root.transform).sizeDelta = new Vector2(48f, 48f);
                var image = root.GetComponent<Image>();
                image.raycastTarget = false;
                image.color = new Color(0.35f, 0.75f, 1f, 1f);
                break;
            case AnimationBenchmarkRig.Sprite:
                root = new GameObject(name, typeof(SpriteRenderer));
                root.GetComponent<SpriteRenderer>().sprite = sprites[0];
                break;
            case AnimationBenchmarkRig.Toggle:
                root = new GameObject(name);
                new GameObject("Child").transform.SetParent(root.transform, false);
                break;
            default:
                root = new GameObject(name);
                break;
        }
        return root;
    }

    static GameObject BuildMoveIt(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites, string suffix)
    {
        var root = BuildRig(benchmarkCase, sprites, benchmarkCase.Id + suffix);
        var player = root.AddComponent<MoveItPlayer>();
        var paths = benchmarkCase.Paths;
        var animated = new bool[paths.Length];
        foreach (var clip in benchmarkCase.Clips)
            foreach (var track in clip.Tracks)
                animated[track.Object] = true;

        var serialized = new SerializedObject(player);
        serialized.FindProperty("playOnEnable").boolValue = false;
        serialized.FindProperty("clock").intValue = (int)PlaybackClock.Scaled;
        var slotIds = new StateNodeId[paths.Length];
        var slots = new List<MoveItSlot>();
        for (var i = 0; i < paths.Length; i++)
        {
            if (!animated[i]) continue;
            var target = paths[i].Length == 0 ? root.transform : root.transform.Find(paths[i]);
            var slot = MoveItSlot.Create(paths[i].Length == 0 ? "Root" : target.name, target);
            slotIds[i] = slot.Id;
            slots.Add(slot);
        }
        WriteArray(serialized.FindProperty("slots"), slots);

        var clips = serialized.FindProperty("clips");
        clips.arraySize = benchmarkCase.Clips.Length;
        for (var c = 0; c < benchmarkCase.Clips.Length; c++)
            clips.GetArrayElementAtIndex(c).boxedValue = CreateMoveItClip(benchmarkCase, c, slotIds, sprites);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    static MoveItClip CreateMoveItClip(AnimationBenchmarkCase benchmarkCase, int index, StateNodeId[] slotIds,
        Sprite[] sprites)
    {
        var spec = benchmarkCase.Clips[index];
        var built = new MoveItClip(spec.Name, spec.Length)
        {
            Cycles = spec.Loop ? MotionCycles.Infinite : 1,
            Repeat = MoveItRepeat.Restart,
        };
        if (Array.Exists(spec.Tracks, track => track.Property == AnimationBenchmarkProperty.Sprite))
            foreach (var sprite in sprites)
                built.AddObject(sprite);
        for (var t = 0; t < spec.Tracks.Length; t++)
        {
            var track = spec.Tracks[t];
            var channel = built.AddChannel(slotIds[track.Object], new MoveItPropertyToken(PropertyOf(track.Property)),
                (MoveItComponentMask)((1 << track.Components) - 1));
            for (var component = 0; component < track.Components; component++)
            {
                var curve = AnimationBenchmarkSpec.Curve(benchmarkCase, index, t, component);
                for (var k = 0; k < curve.Times.Length; k++)
                    built.SetKey(channel, component, curve.Stepped
                        ? new MoveItKey(curve.Times[k], curve.Values[k], MoveItInterpolation.Constant)
                        {
                            tangentMode = MoveItTangentMode.Free,
                        }
                        : new MoveItKey(curve.Times[k], curve.Values[k])
                        {
                            inSlope = curve.Slopes[k],
                            outSlope = curve.Slopes[k],
                            tangentMode = MoveItTangentMode.Free,
                        });
            }
        }
        return built;
    }

    static MoveItProperty PropertyOf(AnimationBenchmarkProperty property) => property switch
    {
        AnimationBenchmarkProperty.LocalPosition => MoveItProperties.Transform.LocalPosition,
        AnimationBenchmarkProperty.LocalEuler => MoveItProperties.Transform.LocalEulerAngles,
        AnimationBenchmarkProperty.LocalScale => MoveItProperties.Transform.LocalScale,
        AnimationBenchmarkProperty.AnchoredPosition => MoveItProperties.RectTransform.AnchoredPosition,
        AnimationBenchmarkProperty.GraphicAlpha => MoveItProperties.Graphic.Alpha,
        AnimationBenchmarkProperty.SpriteAlpha => MoveItProperties.SpriteRenderer.Alpha,
        AnimationBenchmarkProperty.Sprite => MoveItProperties.SpriteRenderer.Sprite,
        AnimationBenchmarkProperty.Active => MoveItProperties.Transform.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };

    static void WriteArray<T>(SerializedProperty array, List<T> items)
    {
        array.arraySize = items.Count;
        for (var i = 0; i < items.Count; i++)
            array.GetArrayElementAtIndex(i).boxedValue = items[i];
    }

    static AnimationClip CreateClip(AnimationBenchmarkCase benchmarkCase, int index, bool legacy, Sprite[] sprites,
        string path)
    {
        var spec = benchmarkCase.Clips[index];
        var paths = benchmarkCase.Paths;
        var clip = new AnimationClip { frameRate = FrameRate(spec), legacy = legacy };
        var bindings = new List<EditorCurveBinding>();
        var curves = new List<AnimationCurve>();
        for (var t = 0; t < spec.Tracks.Length; t++)
        {
            var track = spec.Tracks[t];
            for (var component = 0; component < track.Components; component++)
            {
                var curve = AnimationBenchmarkSpec.Curve(benchmarkCase, index, t, component);
                if (track.Property == AnimationBenchmarkProperty.Sprite)
                {
                    var frames = new ObjectReferenceKeyframe[curve.Times.Length];
                    for (var k = 0; k < frames.Length; k++)
                        frames[k] = new ObjectReferenceKeyframe
                        {
                            time = curve.Times[k],
                            value = sprites[(int)curve.Values[k]],
                        };
                    AnimationUtility.SetObjectReferenceCurve(clip,
                        EditorCurveBinding.PPtrCurve(paths[track.Object], typeof(SpriteRenderer), "m_Sprite"), frames);
                    continue;
                }

                var keys = new Keyframe[curve.Times.Length];
                for (var k = 0; k < keys.Length; k++)
                    keys[k] = curve.Stepped
                        ? new Keyframe(curve.Times[k], curve.Values[k], float.PositiveInfinity, float.PositiveInfinity)
                        : new Keyframe(curve.Times[k], curve.Values[k], curve.Slopes[k], curve.Slopes[k]);
                bindings.Add(Binding(paths[track.Object], track.Property, component));
                curves.Add(new AnimationCurve(keys));
            }
        }
        AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());

        if (legacy)
        {
            clip.wrapMode = spec.Loop ? WrapMode.Loop : WrapMode.ClampForever;
        }
        else
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = spec.Loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    /// <summary>
    /// Sample rate of a clip. Unity extends a clip with object reference keys by one frame past its last key, so a
    /// flipbook's clip runs at the flipbook's rate, which makes that frame the last sprite's own.
    /// </summary>
    static float FrameRate(AnimationBenchmarkClip spec)
    {
        foreach (var track in spec.Tracks)
            if (track.Property == AnimationBenchmarkProperty.Sprite)
                return AnimationBenchmarkSpec.SpriteRate;
        return 60f;
    }

    static EditorCurveBinding Binding(string path, AnimationBenchmarkProperty property, int component)
    {
        var axis = "xyz"[component];
        return property switch
        {
            AnimationBenchmarkProperty.LocalPosition =>
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + axis),
            AnimationBenchmarkProperty.LocalEuler =>
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw." + axis),
            AnimationBenchmarkProperty.LocalScale =>
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + axis),
            AnimationBenchmarkProperty.AnchoredPosition =>
                EditorCurveBinding.FloatCurve(path, typeof(RectTransform), "m_AnchoredPosition." + axis),
            AnimationBenchmarkProperty.GraphicAlpha =>
                EditorCurveBinding.FloatCurve(path, typeof(Image), "m_Color.a"),
            AnimationBenchmarkProperty.SpriteAlpha =>
                EditorCurveBinding.FloatCurve(path, typeof(SpriteRenderer), "m_Color.a"),
            AnimationBenchmarkProperty.Active =>
                EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
            _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
        };
    }

    static AvatarMask CreateMask(AnimationBenchmarkCase benchmarkCase, string path)
    {
        var paths = benchmarkCase.Paths;
        var mask = new AvatarMask { transformCount = paths.Length };
        for (var i = 0; i < paths.Length; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, AnimationBenchmarkSpec.InSubtree(i, benchmarkCase.LayerRoot));
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }

    static AnimatorController CreateController(AnimationBenchmarkCase benchmarkCase, AnimationClip[] clips,
        AvatarMask mask, string path)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine = controller.layers[0].stateMachine;
        for (var c = 0; c < clips.Length; c++)
        {
            if (c == benchmarkCase.LayerClip) continue;
            var state = machine.AddState(benchmarkCase.Clips[c].Name);
            state.motion = clips[c];
            if (c == 0) machine.defaultState = state;
        }
        if (benchmarkCase.LayerClip >= 0) AddLayer(benchmarkCase, controller, clips, mask);
        AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    static void AddLayer(AnimationBenchmarkCase benchmarkCase, AnimatorController controller, AnimationClip[] clips,
        AvatarMask mask)
    {
        controller.AddLayer(benchmarkCase.Clips[benchmarkCase.LayerClip].Name);
        var layers = controller.layers;
        var layer = layers[1];
        layer.defaultWeight = 1f;
        layer.avatarMask = mask;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        var upper = layer.stateMachine.AddState(benchmarkCase.Clips[benchmarkCase.LayerClip].Name);
        upper.motion = clips[benchmarkCase.LayerClip];
        layer.stateMachine.defaultState = upper;
        controller.layers = layers;
    }

    static GameObject BuildAnimator(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites,
        RuntimeAnimatorController controller)
    {
        var root = BuildRig(benchmarkCase, sprites, benchmarkCase.Id + " (Animator)");
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        return root;
    }

    static GameObject BuildLegacy(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites, AnimationClip[] clips)
    {
        var root = BuildRig(benchmarkCase, sprites, benchmarkCase.Id + " (Legacy)");
        var animation = root.AddComponent<Animation>();
        AnimationUtility.SetAnimationClips(animation, clips);
        animation.clip = clips[0];
        animation.playAutomatically = false;
        animation.cullingType = AnimationCullingType.AlwaysAnimate;
        return root;
    }

    static GameObject BuildPlayables(AnimationBenchmarkCase benchmarkCase, Sprite[] sprites)
    {
        var root = BuildRig(benchmarkCase, sprites, benchmarkCase.Id + " (Playables)");
        var animator = root.AddComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        return root;
    }

    /// <summary>
    /// Saves <paramref name="root"/> as a prefab whose players keep their clips once, inside the prefab file, the way
    /// MoveIt stores the clips of every prefab it saves.
    /// </summary>
    static GameObject SaveShared(GameObject root, string path)
    {
        SavePrefab(root, path);
        MoveItClipSharing.Share(path);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    /// <summary>
    /// Saves <paramref name="root"/> as <see cref="SaveShared"/> does, then compresses every clip stored in the prefab
    /// file within a hundredth of a millimetre at the bones' shells, ten times finer than MoveIt's default, which keeps
    /// every rotation within the result check's tolerance.
    /// </summary>
    static GameObject SaveCompressed(GameObject root, string path)
    {
        var prefab = SaveShared(root, path);
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is not MoveItClipAsset clip) continue;
            clip.Compress(new MoveItCompression(0.00001f, 0.03f, 0.01f, 0.0001f), prefab.transform);
            EditorUtility.SetDirty(clip);
        }
        AssetDatabase.SaveAssets();
        return prefab;
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        try
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out var saved);
            return saved ? prefab : throw new InvalidOperationException($"Saving the prefab '{path}' failed.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void CreateScene(AnimationBenchmarkContent catalog)
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.09f, 0.13f, 0f);
            new GameObject("BenchmarkRunner", typeof(BenchmarkRunner));
            var suite = new GameObject("AnimationBenchmark", typeof(AnimationBenchmarkSuite));
            var serialized = new SerializedObject(suite.GetComponent<AnimationBenchmarkSuite>());
            serialized.FindProperty("content").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Saving the scene '{ScenePath}' failed.");
        }
        finally
        {
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
