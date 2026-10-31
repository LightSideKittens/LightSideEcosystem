using System.Collections;
using LightSide;
using UnityEngine;

[AddComponentMenu("UniText/Read More Text")]
public sealed class ReadMoreText : MonoBehaviour
{
    [SerializeField]
    private UniTextBase text;

    [SerializeField]
    private string bodyLabel;

    [SerializeField]
    private string affixLabel;

    [SerializeField]
    private TextUnit revealUnit = TextUnit.Word;

    [SerializeField, Min(0f)]
    private float unitsPerSecond = 14f;

    private RevealModifier reveal;
    private LinkModifier link;
    private OwnedParameterSet<RevealModifier, UnitValue> bodyFront;
    private OwnedParameterSet<RevealModifier, TextUnit> bodyUnit;
    private OwnedParameterSet<RevealModifier, UnitValue> affixFront;
    private TextRange bodySpan;
    private Coroutine running;

    private bool warned;

    private void Reset() => text = GetComponent<UniTextBase>();

    private void OnEnable()
    {
        if (text == null) text = GetComponent<UniTextBase>();
        if (text == null)
        {
            Debug.LogWarning($"[{nameof(ReadMoreText)}] No text to expand.", this);
            enabled = false;
            return;
        }

        text.LayoutCommitted += Bind;
    }

    private void OnDisable()
    {
        if (text != null) text.LayoutCommitted -= Bind;
        if (link != null) link.LinkClicked -= OnLinkClicked;
        bodyFront?.Release();
        bodyUnit?.Release();
        affixFront?.Release();
        bodyFront = null;
        bodyUnit = null;
        affixFront = null;
        link = null;
        reveal = null;
    }
    
    private void Bind()
    {
        reveal = text.GetModifier<RevealModifier>();
        link = text.GetModifier<LinkModifier>();
        if (reveal == null || link == null || !text.TryGetLabeled(bodyLabel, out bodySpan))
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning(
                $"[{nameof(ReadMoreText)}] Needs a link and a reveal range labeled '{bodyLabel}'.",
                this);
            return;
        }

        bodyFront = RangeQuery.For(reveal).WhereLabel(bodyLabel)
            .Own(RevealModifier.Param.Front);
        bodyUnit = RangeQuery.For(reveal).WhereLabel(bodyLabel)
            .Own(RevealModifier.Param.Unit);
        if (!string.IsNullOrEmpty(affixLabel))
            affixFront = RangeQuery.For(reveal).WhereLabel(affixLabel)
                .Own(RevealModifier.Param.Front);

        link.LinkClicked += OnLinkClicked;
        text.LayoutCommitted -= Bind;
    }

    private void OnLinkClicked(string _)
    {
        if (bodyFront == null || running != null) return;
        running = StartCoroutine(Expand());
    }
    
    private IEnumerator Expand()
    {
        var total = text.CountUnits(revealUnit, bodySpan);
        if (total <= 0) yield break;

        var shown = text.CountUnits(revealUnit, reveal.VisibleRangeIn(bodySpan));
        var percent = 100f * shown / total;

        bodyUnit.Value = revealUnit;
        bodyFront.Value = UnitValue.Percent(percent);

        var percentPerSecond = unitsPerSecond * 100f / total;
        while (percentPerSecond > 0f && percent < 100f)
        {
            percent = Mathf.Min(100f, percent + Time.unscaledDeltaTime * percentPerSecond);
            bodyFront.Value = UnitValue.Percent(percent);
            yield return null;
        }

        bodyFront.Value = UnitValue.Percent(100f);
        if (affixFront != null) affixFront.Value = UnitValue.Percent(0f);
        running = null;
    }
}
