using System;
using LightSide;
using Unity.Collections;
using UnityEngine;
using static LightSide.EasingType;
using static LightSide.MotionCycle;
using Random = UnityEngine.Random;

/// <summary>
/// The sinks and configuration verbs the other scenarios do not reach: a native lane buffer written straight
/// by the engine, a settling <see cref="Animated{T}"/> value, a relative offset, and a speed-derived duration.
/// </summary>
[Serializable]
public sealed class SinkGamutScenario : MoveItPlaygroundScenario
{
    private NativeArray<MoveItLane> lanes;
    private Animated<Vector3> settling;
    private Transform bufferDriven;
    private Transform animated;
    private Transform relative;
    private Transform paced;
    private float nextAim;
    private bool bufferMotionLive;

    public override string Title => "Sink gamut";

    public override string Watch =>
        "Left: a native lane buffer the engine writes directly. Middle: an Animated<T> re-aimed mid-settle. " +
        "Right: a relative offset and a speed-paced move.";

    public override void Enter(MoveItStage stage)
    {
        lanes = new NativeArray<MoveItLane>(4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        bufferDriven = stage.Spawn(PrimitiveType.Cube, new Vector3(-4.5f, 0f, 0f),
            new Color(0.5f, 1f, 1f), "BufferDriven");
        animated = stage.Spawn(PrimitiveType.Sphere, new Vector3(-1.5f, 0f, 0f),
            new Color(1f, 0.8f, 0.4f), "Animated");
        relative = stage.Spawn(PrimitiveType.Capsule, new Vector3(1.5f, 0f, 0f),
            new Color(0.8f, 0.5f, 1f), "Relative");
        paced = stage.Spawn(PrimitiveType.Cube, new Vector3(4.5f, 0f, 0f),
            new Color(0.6f, 1f, 0.6f), "AtSpeed");

        stage.Label(new Vector3(-4.5f, -2f, 0f), "native lane buffer", 0.16f);
        stage.Label(new Vector3(-1.5f, -2f, 0f), "Animated<T> spring", 0.16f);
        stage.Label(new Vector3(1.5f, -2f, 0f), "Relative offset", 0.16f);
        stage.Label(new Vector3(4.5f, -2f, 0f), "AtSpeed pacing", 0.16f);

        settling = new Animated<Vector3>(animated.position, new Spring(140f, 14f));
        nextAim = 0f;

        stage.CheckSurvives(() =>
                relative.MoveYTo(1.5f, SineInOut.Over(1.1f)).Relative()
                    .Loop(),
            "declaring a motion relative after creation");

        stage.CheckSurvives(() =>
                paced.MoveXTo(paced.position.x - 3f, 1f).AtSpeed(2f)
                    .Loop(),
            "deriving a duration from units per second");

        stage.CheckThrows<InvalidOperationException>(
            () => paced.MoveYTo(1f, new Spring(120f, 10f)).AtSpeed(2f),
            "pacing a spring, which owns its own duration");

        stage.CheckThrows<ArgumentOutOfRangeException>(
            () => paced.MoveZTo(1f, 1f).AtSpeed(0f),
            "pacing at zero units per second");

        stage.Say("four sinks and two configuration verbs on one stage");
    }

    public override void Tick(MoveItStage stage)
    {
        if (!bufferMotionLive && lanes.IsCreated)
        {
            stage.CheckSurvives(() =>
                    MoveIt.Into(lanes, 0, new Vector3(-4.5f, -1.5f, 0f), new Vector3(-4.5f, 2f, 0f),
                        QuadraticInOut.Over(1.3f)).Loop(),
                "driving a native lane buffer directly");
            bufferMotionLive = true;

            stage.CheckThrows<ArgumentOutOfRangeException>(
                () => MoveIt.Into(lanes, 99, Vector3.zero, Vector3.one, 1f),
                "writing past the end of the lane buffer");
        }

        if (lanes.IsCreated && bufferDriven != null)
            bufferDriven.position = lanes[0].vector3;

        if (animated != null)
        {
            animated.position = settling.Value;
            if (stage.Elapsed >= nextAim)
            {
                nextAim = stage.Elapsed + 1.1f;
                settling.Target = new Vector3(-1.5f, Random.Range(-1.5f, 2f), 0f);
                stage.Say($"Animated<Vector3> re-aimed; settled = {settling.IsSettled}");
            }
        }

        stage.Check(!lanes.IsCreated || lanes.Length == 4, "the lane buffer keeps its shape under a live motion");
    }

    public override void Exit()
    {
        MoveIt.StopAll();
        settling.Stop();
        if (lanes.IsCreated) lanes.Dispose();
        bufferMotionLive = false;
    }
}

/// <summary>Engine components rather than transforms: a camera, a light and a material instance.</summary>
[Serializable]
public sealed class ComponentGamutScenario : MoveItPlaygroundScenario
{
    private Camera camera;
    private Light light;
    private Material material;
    private float restoreFov;
    private float restoreShadow;

    public override string Title => "Component gamut";

    public override string Watch =>
        "The camera breathes, the light's shadows fade, and a material instance scrolls its own texture.";

    public override void Enter(MoveItStage stage)
    {
        for (var i = 0; i < 5; i++)
        {
            var cube = stage.Spawn(PrimitiveType.Cube, new Vector3(i * 1.6f - 3.2f, 0f, 0f),
                new Color(0.7f, 0.75f, 0.85f), $"Backdrop{i}");
            cube.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            cube.RotateLocalTo(new Vector3(0f, 360f, 0f), 4f + i * 0.3f)
                .Loop(Incremental);
        }

        camera = Camera.main;
        if (camera != null)
        {
            restoreFov = camera.fieldOfView;
            stage.CheckSurvives(
                () => camera.FovTo(restoreFov * 1.35f, SineInOut.Over(2.2f))
                    .Loop(),
                "animating the camera field of view");
        }

        light = ObjectUtils.FindAny<Light>();
        if (light != null)
        {
            restoreShadow = light.shadowStrength;
            stage.CheckSurvives(
                () => light.ShadowStrengthTo(0.1f, QuadraticInOut.Over(1.7f))
                    .Loop(),
                "animating light shadow strength");
        }

        var host = stage.Spawn(PrimitiveType.Sphere, new Vector3(0f, 2.2f, 0f), Color.white, "MaterialHost");
        var renderer = host.GetComponent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            material = new Material(renderer.sharedMaterial) { name = "Playground Material Instance" };
            renderer.material = material;
            stage.CheckSurvives(
                () => material.MainTextureOffsetTo(new Vector2(1f, 1f), 3f)
                    .Loop(Incremental),
                "scrolling a material's main texture offset");
        }

        stage.Say("camera, light and a material instance are all sinks like any other");
    }

    public override void Exit()
    {
        MoveIt.StopAll();
        if (camera != null) camera.fieldOfView = restoreFov;
        if (light != null) light.shadowStrength = restoreShadow;
        ObjectUtils.SafeDestroy(material);
        camera = null;
        light = null;
        material = null;
    }
}

/// <summary>Channel combinations the transform driver is documented to refuse.</summary>
[Serializable]
public sealed class ChannelRefusalScenario : MoveItPlaygroundScenario
{
    private Transform target;
    private float nextRound;

    public override string Title => "Channel refusals";

    public override bool IsStress => true;

    public override string Watch =>
        "A partial channel group is not a drivable claim; the engine must refuse it rather than half-apply it.";

    public override void Enter(MoveItStage stage)
    {
        target = stage.Spawn(PrimitiveType.Cube, Vector3.zero, new Color(1f, 0.6f, 0.2f), "Channels");
        nextRound = 0f;
    }

    public override void Tick(MoveItStage stage)
    {
        if (target == null || stage.Elapsed < nextRound) return;
        nextRound = stage.Elapsed + 1f;
        var driven = target;

        stage.CheckThrows<ArgumentException>(
            () => MoveIt.Drive(driven, MoveItTransformChannel.LocalPositionX | MoveItTransformChannel.LocalPositionY,
                Vector3.zero, Vector3.one, 0.5f),
            "driving half of a position group");

        stage.CheckThrows<ArgumentException>(
            () => MoveIt.Drive(driven, MoveItTransformChannel.All, Vector3.zero, Vector3.one, 0.5f),
            "driving every channel at once through one claim");

        stage.CheckSurvives(
            () => MoveIt.Drive(driven, MoveItTransformChannel.LocalPosition, driven.localPosition,
                driven.localPosition + Vector3.up, 0.5f),
            "driving one complete channel group");
    }

    public override void Exit() => target = null;
}
