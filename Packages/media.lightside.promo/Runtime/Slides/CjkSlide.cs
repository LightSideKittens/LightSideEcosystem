using UnityEngine;

namespace LightSide.Promo
{
    /// <summary>
    /// A wall of Han characters, Hangul and kana, none of which is in the project.
    /// </summary>
    /// <remarks>
    /// The specimen carries no font, so the system cascade resolves every character in it. Assigning one would make
    /// the shot a picture of the claim rather than the claim itself.
    /// <para>
    /// Volume is the argument. A Latin sample proves nothing about CJK, because Latin is the case every engine gets
    /// right and CJK is the one that makes a baked atlas enormous: the cost a project avoids is proportional to how
    /// many distinct glyphs it never has to bake, and the only way to show a count that large is to fill the frame
    /// with it.
    /// </para>
    /// <para>
    /// One paragraph, with no break authored anywhere in it. Every line the viewer sees was decided by the engine,
    /// which is the second thing the shot proves: Han and kana carry no spaces to break on, so a wall that wraps
    /// evenly is a wall broken by the line-breaking rules rather than by a space. An authored break would have hidden
    /// exactly that, and justification would then be arranging lines somebody else had already chosen.
    /// </para>
    /// <para>
    /// Real passages close the specimen — a Tang quatrain and a line of mixed kanji and kana — because a chart alone
    /// shows coverage while a sentence shows that the coverage composes.
    /// </para>
    /// <para>
    /// It states no megabytes. <see cref="BuildSizeSlide"/> is where the film quotes what a CJK face costs, and a
    /// second figure for the same thing on an earlier frame reads as the film disagreeing with itself however
    /// carefully each one is worded. This shot shows the glyphs; that one prices them.
    /// </para>
    /// </remarks>
    [AddComponentMenu(PromoMenu.AddComponent.Slides + nameof(CjkSlide))]
    public sealed class CjkSlide : Slide
    {
        [SerializeField] private string headline = "Ship in Chinese, Japanese and Korean.";
        [SerializeField] private string sub = "Still no bigger build. No font files. No baked atlas.";
        [SerializeField] private string payoff = "Every one of them came from the operating system.";

        /// <summary>
        /// The wall. Authored as a specimen, so a character that fails is visible on its own rather than hidden
        /// inside a word.
        /// </summary>
        [SerializeField, TextArea(6, 16)]
        private string specimen =
            "日月火水木金土山川田人口手足目耳鼻舌歯首心身体力気血肉骨皮毛" +
            "雨雪雲風雷霜露虹星空海洋湖池河谷岩石砂原野林森竹松梅桜菊蘭" +
            "犬猫馬牛羊豚鶏鳥魚亀蛇虫蝶蜂蟻鹿熊狼狐猿象虎豹鯨鮫鷲鷹燕鳩" +
            "行走飛泳読書話聞見食飲寝起座立歩考思知覚感動働遊学教習練修" +
            "国家族村町市県都府省庁院校館店屋堂塔橋道路駅港軍警官民法権" +
            "愛情義理仁徳智勇誠信忠孝礼節恩恥怒哀楽喜悲驚恐望夢希志念" +
            "一二三四五六七八九十百千万億兆年週暦時分秒春夏秋冬朝昼夜曜" +
            "東西南北上下左右前後内外中央高低長短大小多少新古遠近深浅" +
            "色赤青白黒黄緑紫紺朱藍緋橙銀銅鉄鋼鉛錫炭酸塩糖油脂粉綿絹麻" +
            "刀剣弓矢盾鎧兜槍斧鎌鍬鋤釘錐鋸鑿槌鍋釜皿椀箸匙杯瓶壺籠箱" +
            "開閉始終断続増減進退昇降往復送迎買売貸借貯蓄費払受取渡。" +
            "简体中文和繁體中文同時顯示，一個系統字體就夠了。" +
            "春眠不覚暁，処処聞啼鳥，夜来風雨声，花落知多少。" +
            "漢字仮名交じり文、ひらがなとカタカナも同じ流れで組まれる。" +
            "안녕하세요 세계, 한국어 글자는 자모가 모여 음절이 됩니다.";

        private Claim claim;
        private Claim note;
        private Showcase panel;
        private GlyphReveal reveal;

        protected override void OnBuild(Stage stage)
        {
            stage.Backdrop(stage.Root);

            claim = stage.Claim(stage.Root, headline, sub);
            note = stage.Claim(stage.Root, payoff, top: false);

            panel = stage.Showcase("Han", stage.Root, null,
                specimen, stage.ContentHeight * 0.11f,
                horizontal: HorizontalAlignment.Justify, vertical: VerticalAlignment.Top);

            reveal = stage.Reveal(panel.Body, new FadeRevealHandler());

            Cue("writeon", First);
            Cue("settled", First + WriteOn);
        }

        protected override void OnRender(float local)
        {
            claim.Pose(local);
            panel.Pose(local - First * 0.5f);
            reveal.Fill = GlyphReveal.Frontier.Window(local, First, WriteOn);
            note.Pose(local - Payoff);
        }

        /// <summary>
        /// How long the wall takes to sweep in.
        /// </summary>
        /// <remarks>
        /// Long for a reveal, and deliberately: the frontier crossing several hundred glyphs is what a viewer reads
        /// as "there are a great many of these", which is the shot's whole claim. Shortened, it lands as a fade and
        /// the count stops registering.
        /// </remarks>
        private const float WriteOn = 2.6f;

        private const float First = 0.5f;

        /// <summary>When the closing line arrives, after the wall has finished filling.</summary>
        private const float Payoff = 3.4f;
    }
}
