#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using LightSide;
using Debug = UnityEngine.Debug;

public class UnicodeDataGeneratorConfig : ScriptableObject
{
    [Header("Source Files")] public TextAsset derivedBidiClassAsset;
    public TextAsset derivedJoiningTypeAsset;
    public TextAsset arabicShapingAsset;
    public TextAsset bidiBracketsAsset;
    public TextAsset scriptsAsset;
    public TextAsset lineBreakAsset;
    public TextAsset emojiDataAsset;
    public TextAsset generalCategoryAsset;
    public TextAsset eastAsianWidthAsset;
    public TextAsset graphemeBreakPropertyAsset;
    public TextAsset wordBreakPropertyAsset;
    public TextAsset derivedCorePropertiesAsset;
    public TextAsset scriptExtensionsAsset;
    public TextAsset unicodeDataAsset;

    [Header("Output")] public DefaultAsset outputFolder;
    public string outputFileName = "UnicodeData.bytes";

    [Header("Testing")] public TestData testing = new();


    public void OnGUI()
    {
        EditorGUILayout.LabelField("Unicode Data Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Source Files (Required)", EditorStyles.boldLabel);

        derivedBidiClassAsset = (TextAsset)EditorGUILayout.ObjectField(
            "DerivedBidiClass.txt",
            derivedBidiClassAsset,
            typeof(TextAsset),
            false);

        derivedJoiningTypeAsset = (TextAsset)EditorGUILayout.ObjectField(
            "DerivedJoiningType.txt",
            derivedJoiningTypeAsset,
            typeof(TextAsset),
            false);

        arabicShapingAsset = (TextAsset)EditorGUILayout.ObjectField(
            "ArabicShaping.txt",
            arabicShapingAsset,
            typeof(TextAsset),
            false);

        bidiBracketsAsset = (TextAsset)EditorGUILayout.ObjectField(
            "BidiBrackets.txt",
            bidiBracketsAsset,
            typeof(TextAsset),
            false);

        scriptsAsset = (TextAsset)EditorGUILayout.ObjectField("Scripts.txt", scriptsAsset, typeof(TextAsset), false);
        lineBreakAsset = (TextAsset)EditorGUILayout.ObjectField("LineBreak.txt", lineBreakAsset, typeof(TextAsset), false);
        emojiDataAsset = (TextAsset)EditorGUILayout.ObjectField("emoji-data.txt", emojiDataAsset, typeof(TextAsset), false);
        generalCategoryAsset = (TextAsset)EditorGUILayout.ObjectField("DerivedGeneralCategory.txt", generalCategoryAsset, typeof(TextAsset), false);
        eastAsianWidthAsset = (TextAsset)EditorGUILayout.ObjectField("EastAsianWidth.txt", eastAsianWidthAsset, typeof(TextAsset), false);
        graphemeBreakPropertyAsset = (TextAsset)EditorGUILayout.ObjectField("GraphemeBreakProperty.txt", graphemeBreakPropertyAsset, typeof(TextAsset), false);
        wordBreakPropertyAsset = (TextAsset)EditorGUILayout.ObjectField("WordBreakProperty.txt", wordBreakPropertyAsset, typeof(TextAsset), false);
        derivedCorePropertiesAsset = (TextAsset)EditorGUILayout.ObjectField("DerivedCoreProperties.txt", derivedCorePropertiesAsset, typeof(TextAsset), false);
        scriptExtensionsAsset = (TextAsset)EditorGUILayout.ObjectField("ScriptExtensions.txt", scriptExtensionsAsset, typeof(TextAsset), false);
        unicodeDataAsset = (TextAsset)EditorGUILayout.ObjectField("UnicodeData.txt", unicodeDataAsset, typeof(TextAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
        outputFileName = EditorGUILayout.TextField("Output File Name", outputFileName);

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(this);

        EditorGUILayout.Space();

        var canGenerate = derivedBidiClassAsset != null && derivedJoiningTypeAsset != null &&
                          arabicShapingAsset != null && bidiBracketsAsset != null &&
                          scriptsAsset != null &&
                          lineBreakAsset != null && emojiDataAsset != null &&
                          generalCategoryAsset != null && eastAsianWidthAsset != null &&
                          graphemeBreakPropertyAsset != null && wordBreakPropertyAsset != null && derivedCorePropertiesAsset != null &&
                          scriptExtensionsAsset != null && unicodeDataAsset != null &&
                          outputFolder != null;

        EditorGUI.BeginDisabledGroup(!canGenerate);
        if (GUILayout.Button("Generate Unicode Data", GUILayout.Height(30))) GenerateUnicodeData();
        EditorGUI.EndDisabledGroup();

        if (!canGenerate)
            EditorGUILayout.HelpBox(
                "Assign all required source files (including UnicodeData.txt, GraphemeBreakProperty.txt, and WordBreakProperty.txt) and an output folder to generate.",
                MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);
        testing.OnGui();
    }

    private void GenerateUnicodeData()
    {
        try
        {
            var folderPath = AssetDatabase.GetAssetPath(outputFolder);
            var outputPath = Path.Combine(folderPath, outputFileName);

            var tempDir = Path.Combine(Application.temporaryCachePath, "UnicodeGen");
            Directory.CreateDirectory(tempDir);

            var derivedBidiPath = SaveTempFile(tempDir, "DerivedBidiClass.txt", derivedBidiClassAsset);
            var derivedJoiningPath = SaveTempFile(tempDir, "DerivedJoiningType.txt", derivedJoiningTypeAsset);
            var arabicShapingPath = SaveTempFile(tempDir, "ArabicShaping.txt", arabicShapingAsset);
            var bidiBracketsPath = SaveTempFile(tempDir, "BidiBrackets.txt", bidiBracketsAsset);
            var scriptsPath = SaveTempFile(tempDir, "Scripts.txt", scriptsAsset);
            var lineBreakPath = SaveTempFile(tempDir, "LineBreak.txt", lineBreakAsset);
            var emojiDataPath = SaveTempFile(tempDir, "emoji-data.txt", emojiDataAsset);
            var generalCategoryPath = SaveTempFile(tempDir, "DerivedGeneralCategory.txt", generalCategoryAsset);
            var eastAsianWidthPath = SaveTempFile(tempDir, "EastAsianWidth.txt", eastAsianWidthAsset);
            var graphemeBreakPath = SaveTempFile(tempDir, "GraphemeBreakProperty.txt", graphemeBreakPropertyAsset);
            var wordBreakPath = SaveTempFile(tempDir, "WordBreakProperty.txt", wordBreakPropertyAsset);
            var derivedCorePropertiesPath = SaveTempFile(tempDir, "DerivedCoreProperties.txt", derivedCorePropertiesAsset);
            var scriptExtensionsPath = SaveTempFile(tempDir, "ScriptExtensions.txt", scriptExtensionsAsset);
            var unicodeDataPath = SaveTempFile(tempDir, "UnicodeData.txt", unicodeDataAsset);

            var builder = new UnicodeDataBuilder();
            builder.LoadDerivedBidiClass(derivedBidiPath);
            builder.LoadDerivedJoiningType(derivedJoiningPath);
            builder.LoadArabicShaping(arabicShapingPath);
            builder.LoadScripts(scriptsPath);
            builder.LoadLineBreak(lineBreakPath);
            builder.LoadEmojiData(emojiDataPath);
            builder.LoadGeneralCategory(generalCategoryPath);
            builder.LoadEastAsianWidth(eastAsianWidthPath);
            builder.LoadGraphemeBreakProperty(graphemeBreakPath);
            builder.LoadWordBreakProperty(wordBreakPath);
            builder.LoadIndicConjunctBreak(derivedCorePropertiesPath);
            builder.LoadDefaultIgnorable(derivedCorePropertiesPath);
            builder.LoadScriptExtensions(scriptExtensionsPath);
            builder.LoadUnicodeData(unicodeDataPath);

            var ranges = builder.BuildRangeEntries();
            var brackets = UnicodeDataBuilder.BuildBracketEntries(bidiBracketsPath);
            var scripts = builder.BuildScriptRangeEntries();
            var lineBreaks = builder.BuildLineBreakRangeEntries();
            var extendedPictographics = builder.BuildExtendedPictographicRangeEntries();
            var generalCategories = builder.BuildGeneralCategoryRangeEntries();
            var eastAsianWidths = builder.BuildEastAsianWidthRangeEntries();
            var graphemeBreaks = builder.BuildGraphemeBreakRangeEntries();
            var wordBreaks = builder.BuildWordBreakRangeEntries();
            var indicConjunctBreaks = builder.BuildIndicConjunctBreakRangeEntries();
            var scriptExtensions = builder.GetScriptExtensionEntries();
            var defaultIgnorables = builder.BuildDefaultIgnorableRangeEntries();
            var emojiPresentations = builder.BuildEmojiPresentationRangeEntries();
            var emojiModifierBases = builder.BuildEmojiModifierBaseRangeEntries();
            var caseMappings = builder.BuildCaseMappingEntries();

            UnicodeBinaryWriter.WriteBinary(outputPath, UnicodeDataBuilder.ReadUnicodeVersion(derivedBidiPath),
                ranges, brackets, scripts, lineBreaks,
                extendedPictographics, generalCategories, eastAsianWidths, graphemeBreaks,
                indicConjunctBreaks, scriptExtensions, defaultIgnorables,
                emojiPresentations, emojiModifierBases, caseMappings, wordBreaks);

            Debug.Log($"Generated Unicode data (Format V11) with {ranges.Count} ranges, " +
                      $"{brackets.Count} brackets, " +
                      $"{scripts.Count} script ranges, {lineBreaks.Count} line break ranges, " +
                      $"{extendedPictographics.Count} Extended_Pictographic ranges, " +
                      $"{generalCategories.Count} GeneralCategory ranges, " +
                      $"{eastAsianWidths.Count} EastAsianWidth ranges, " +
                      $"{graphemeBreaks.Count} GraphemeBreak ranges, " +
                      $"{wordBreaks.Count} WordBreak ranges, " +
                      $"{indicConjunctBreaks.Count} InCB ranges, " +
                      $"{scriptExtensions.Count} ScriptExtension entries, " +
                      $"{defaultIgnorables.Count} Default_Ignorable ranges, " +
                      $"{emojiPresentations.Count} Emoji_Presentation ranges, " +
                      $"{emojiModifierBases.Count} Emoji_Modifier_Base ranges, " +
                      $"{caseMappings.Count} CaseMapping entries.");

            Directory.Delete(tempDir, true);
            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to generate Unicode data: {ex}");
        }
    }

    private string SaveTempFile(string dir, string name, TextAsset asset)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, asset.text);
        return path;
    }
}
#endif
