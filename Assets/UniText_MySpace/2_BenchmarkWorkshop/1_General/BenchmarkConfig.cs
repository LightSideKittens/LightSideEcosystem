using System.Text;
using LightSide;
using UnityEngine;

/// <summary>
/// Single source of truth for benchmark workloads, discovered via <see cref="Instance"/>. The text
/// corpora are code constants (never read from a prefab) so a scene edit cannot make one engine's run
/// differ from another's. Counts and the glyph-rasterization text are the inspector-tunable settings.
/// </summary>
public class BenchmarkConfig : MonoBehaviour
{
    [Header("Workload size (shared by every engine)")]
    public int objectCount = 100;
    public int iterations = 10;
    public int warmupIterations = 3;

    [Min(0), Tooltip("Identical untimed cycles after each phase in development builds. One cycle exposes repeat growth; more cycles make the leak trend more reliable.")]
    public int memoryProbeRepeats = 1;

    [Header("Glyph rasterization")]
    [Tooltip("Text the glyph-rasterization benchmark clears and re-renders. Leave empty to use the text baked on each engine's own benchmark object.")]
    [SerializeField, TextArea(3, 10)] string glyphRasterCorpus;

    /// <summary>The inspector-set glyph corpus, or null/empty when unset (callers fall back to their baked text).</summary>
    public string GlyphRasterText => glyphRasterCorpus;

    /// <summary>
    /// ~3.1k-char shaping-stress corpus (plain text ~2k) — weighted ~50% toward Arabic/Persian/Urdu plus
    /// Hebrew, Devanagari and Thai, the scripts whose cursive joining, mark stacking and syllabic
    /// reordering load the shaper hardest (Latin/CJK barely shape). Saturated with the rich-text tags
    /// every engine parses identically — &lt;b&gt; &lt;i&gt; &lt;u&gt; &lt;s&gt; &lt;color=#hex&gt; &lt;size=%&gt; &lt;sub&gt; &lt;sup&gt; &lt;uppercase&gt;
    /// &lt;lowercase&gt; (92 tag pairs). The rich-text phase's source; non-markup phases use <see cref="MultilingualPlain"/>.
    /// </summary>
    public static readonly string Multilingual = string.Join("\n", new[]
    {
        "العربية: <b>اللغةُ العربيةُ</b> من أعرقِ لغاتِ العالمِ، يكتبها <color=#c0392b>مئاتُ الملايين</color> بخطٍّ متّصلٍ.",
        "يتّصلُ <b>الحرفُ</b> بما قبلَه وبما بعدَه، فيتغيّرُ <i>شكلُه</i> حسبَ موضعِه: <color=#2980b9>مبدوءٌ ومتوسّطٌ ومنتهٍ</color>.",
        "الخطُّ <b>العربيُّ</b> فنٌّ أصيلٌ، له <u>قواعدُ دقيقةٌ</u> في النسبةِ و<i>التناسبِ</i> بين الحروفِ.",
        "النَّصُّ <b>المُشَكَّلُ</b> يَحْمِلُ <color=#8e44ad>حَرَكَاتٍ</color> فَوْقَ الحُرُوفِ وَتَحْتَهَا، كالفَتْحَةِ وَالكَسْرَةِ وَالضَّمَّةِ.",
        "تتداخلُ <b>الأرقامُ</b> ١٢٣٤٥٦٧٨٩ مع النصِّ من <color=#e67e22>اليمينِ إلى اليسارِ</color> بسلاسةٍ.",
        "تجعلُ لِيغاتُ <b>لام-ألف</b> (لا) والحركاتُ <i>تشكيلَ</i> النصِّ أكثرَ <u>تعقيداً</u> من اللاتينيةِ.",
        "فارسی: <b>خطِ فارسی</b> بر پایهٔ الفبای عربی است، با حروفِ <color=#16a085>پ چ ژ گ</color> و اتّصالِ <i>خمیده</i>.",
        "اردو: <b>نستعلیق</b> رسمُ الخط پیچیدہ ہے، جہاں حروف <color=#c0392b>ترچھے</color> اور <u>جھکے</u> ہوتے ہیں۔",
        "עברית: <b>הכתב העברי</b> נכתב <color=#2980b9>מימין לשמאל</color>, עם <i>ניקוד</i> מתחת ומעל לאותיות.",
        "המילה <b>שָׁלוֹם</b> עם ניקוד מלא <u>מדגימה</u> מיקום <color=#8e44ad>סימני</color> תנועה מדויק.",
        "हिन्दी: <b>देवनागरी</b> में <color=#c0392b>संयुक्ताक्षर</color> और <i>मात्राएँ</i> अक्षरों के <u>पहले, बाद और ऊपर</u> जुड़ती हैं।",
        "ไทย: <b>ภาษาไทย</b>ไม่มีช่องว่าง การ<color=#e67e22>ตัดคำ</color>ใช้<u>พจนานุกรม</u> และ<i>สระ</i>วางรอบพยัญชนะ.",
        "Latin: <b>AVATAR</b> and <i>WoVeN</i> stress <u>kerning</u>; <color=#e74c3c>ffi ffl fi fl</color> are <size=110%>ligatures</size>.",
        "Русский: <b>кириллица</b> с <i>переносами</i> и <color=#3a86e0>кернингом</color>, «ёлки» и <u>тире</u> — тоже часть картины.",
        "Ελληνικά: <b>τυπογραφία</b> με <i>τόνους</i> ά έ ή, <color=#8e44ad>διαλυτικά</color> ϊ ϋ και <u>τελικό ς</u>.",
        "Science: H<sub>2</sub>O, CO<sub>2</sub>, E = mc<sup>2</sup>, x<sup>n</sup> + y<sub>i</sub>, <uppercase>si</uppercase> <lowercase>UNITS</lowercase>.",
        "Numbers: <color=#e74c3c>3.14159</color>, ١٬٠٠٠٬٠٠٠, <size=90%>−273.15°C</size>, ١٠٠٪ mixed with <b>Latin</b> digits.",
        "القراءةُ من <b>اليمينِ</b> إلى اليسارِ تتطلّبُ <i>خوارزميةً</i> ثنائيةَ الاتجاهِ لمزجِ <color=#c0392b>النصِّ</color> بالأرقامِ.",
        "تختلفُ <b>أشكالُ</b> الحرفِ الواحدِ: <s>معزولٌ</s>، ومتّصلٌ في <color=#2980b9>البدايةِ والوسطِ والنهايةِ</color>.",
        "الحروفُ <b>ذاتُ النقاطِ</b> مثلُ ب ت ث ن ي تحتاجُ <i>ضبطاً</i> دقيقاً <u>لموضعِ النقطةِ</u> فوقَها وتحتَها.",
        "الخطوطُ <b>الحديثةُ</b> تدعمُ <color=#27ae60>الحركاتِ</color> والتشكيلَ الكاملَ في <size=110%>كلِّ الأوزانِ</size>.",
        "فارسی: <b>واژه‌ها</b> با نیم‌فاصله و <i>کشیده</i> نوشته می‌شوند تا <color=#e67e22>خوانا</color> بمانند و <u>زیبا</u>.",
        "संस्कृत: <b>अक्षरों</b> का <color=#c0392b>पुनःक्रमण</color> और <i>जोडाक्षर</i> देवनागरी शेपिंग को <u>कठिन</u> बनाते हैं।",
        "الخطُّ <b>الكوفيُّ</b> والنسخُ و<i>الرقعةُ</i> و<color=#8e44ad>الثلثُ</color> أنماطٌ لها <u>هندستُها</u> الخاصةُ.",
        "تُضبطُ <b>المسافاتُ</b> بين الكلماتِ و<i>الأسطرِ</i> لتحقيقِ <color=#16a085>توازنٍ بصريٍّ</color> على الصفحةِ.",
        "העברית <b>מערבבת</b> <color=#2980b9>אותיות</color> ומספרים 123 <i>בשורה</i> אחת עם <u>כיווניות</u> מעורבת.",
        "الطباعةُ <b>الرقميةُ</b> يجبُ أن تُحاكيَ جمالَ <color=#34495e>الخطِّ اليدويِّ</color> في كلِّ <size=110%>الأحجامِ</size>.",
    });

    /// <summary><see cref="Multilingual"/> with all markup stripped — the workload for the shaping/layout/mesh phases, where tags would otherwise be measured as literal text.</summary>
    public static readonly string MultilingualPlain = System.Text.RegularExpressions.Regex.Replace(Multilingual, "<[^>]+>", "");

    /// <summary>Plain-Latin corpus (~40 paragraphs, no tags, no RTL) — the apples-to-apples case where every engine performs comparable work.</summary>
    public static readonly string Latin = BuildLatin();

    static string BuildLatin()
    {
        var words = new[]
        {
            "performance", "rendering", "glyph", "layout", "measure", "pipeline", "vertex", "buffer",
            "keyboard", "window", "quality", "shipping", "release", "candidate", "feature", "stable",
            "quick", "brown", "fox", "jumps", "over", "the", "lazy", "dog", "sphinx", "of", "black",
            "quartz", "judge", "my", "vow", "pack", "with", "five", "dozen", "liquor", "jugs",
        };
        var sb = new StringBuilder(4096);
        for (int line = 0; line < 40; line++)
        {
            if (line > 0) sb.Append('\n');
            sb.Append(line + 1).Append(". ");
            int count = 8 + (line * 7) % 9;
            for (int w = 0; w < count; w++)
            {
                if (w > 0) sb.Append(' ');
                sb.Append(words[(line * 13 + w * 5) % words.Length]);
            }
            sb.Append(line % 3 == 0 ? '.' : line % 3 == 1 ? '!' : '?');
        }
        return sb.ToString();
    }

    static BenchmarkConfig instance;
    public static BenchmarkConfig Instance => instance != null ? instance : instance = ObjectUtils.FindAny<BenchmarkConfig>();
}
