using System;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Accepts an empty field or an address with a name, an at sign and a dotted domain.</summary>
    [Serializable]
    public sealed class EmailValidator : InputValidatorBase
    {
        [Tooltip("What the field says while the address is not valid.")]
        [SerializeField, StatePassive] private string message = "Enter an address like name@example.com";

        public override ValidationState Validate(ITextDocument document)
        {
            var count = document.CodepointCount;
            if (count == 0) return ValidationState.Valid();
            var at = -1;
            var dot = -1;
            for (var i = 0; i < count; i++)
            {
                var codepoint = document.GetCodepointAt(i);
                if (codepoint == '@')
                {
                    if (at >= 0) return ValidationState.Invalid(message);
                    at = i;
                }
                else if (codepoint == '.' && at >= 0) dot = i;
            }
            return at > 0 && dot > at + 1 && dot < count - 2 ? ValidationState.Valid() : ValidationState.Invalid(message);
        }
    }
}
