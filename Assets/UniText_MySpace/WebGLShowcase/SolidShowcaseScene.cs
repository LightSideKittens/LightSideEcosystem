using System;
using UnityEngine;

namespace LightSide.Showcase
{
    /// <summary>Extruded text a visitor turns by dragging; variants are 3D looks.</summary>
    public sealed class SolidShowcaseScene : TextShowcaseScene
    {
        private const float DegreesPerScreen = 160f;
        private const float MaxPitch = 35f;
        private const float ReturnRate = 1.6f;
        private const float SwingRate = 1.1f;
        private const float SwingDegrees = 26f;

        /// <summary>The look a variant gives the solid.</summary>
        [Serializable]
        public struct Look
        {
            public string variant;
            public Text3DLook look;
        }

        [SerializeField] private UniText3D solid;
        [SerializeField] private Transform turntable;
        [SerializeField] private Camera view;
        [SerializeField] private Look[] looks;
        [SerializeField] private DragHint hint;

        [Tooltip("Camera aspect the stage is framed for; a narrower camera shrinks the stage to keep it in frame.")]
        [SerializeField] private float designAspect = 1.5f;

        private Vector2 dragged;
        private Vector2 pointer;
        private bool dragging;
        private float swung;
        private int ownAntiAliasing;

        private void OnEnable()
        {
            ownAntiAliasing = QualitySettings.antiAliasing;
            QualitySettings.antiAliasing = 4;
            view.forceIntoRenderTexture = true;
            Cursors.Set(this, CursorType.Grab);
        }

        private void OnDisable()
        {
            QualitySettings.antiAliasing = ownAntiAliasing;
            view.forceIntoRenderTexture = false;
            Cursors.Clear(this);
        }

        public override void SetVariant(string id)
        {
            var index = Array.FindIndex(looks, candidate => candidate.variant == id);
            if (index < 0) throw new ArgumentException($"Showcase scene '{Id}' has no look '{id}'.", nameof(id));
            looks[index].look.ApplyTo(solid);
            base.SetVariant(id);
        }

        protected override void Update()
        {
            base.Update();
            turntable.localScale = Vector3.one * Mathf.Min(1f, view.aspect / designAspect);
            Drag();
            if (!dragging) swung += Time.deltaTime;
            var swing = Mathf.Sin(swung * SwingRate);
            hint.Sweep(swing);
            turntable.localRotation = Quaternion.Euler(
                4f * Mathf.Sin(swung * 0.5f) + dragged.y,
                -SwingDegrees * swing + dragged.x, 0f);
        }

        private void Drag()
        {
            if (!InputUtils.GetPointerPressed())
            {
                if (dragging) Cursors.Set(this, CursorType.Grab);
                dragging = false;
                dragged = Vector2.Lerp(dragged, Vector2.zero, 1f - Mathf.Exp(-Time.deltaTime * ReturnRate));
                return;
            }
            var position = InputUtils.MousePosition;
            if (dragging)
            {
                var delta = (position - pointer) / Screen.height * DegreesPerScreen;
                dragged.x -= delta.x;
                dragged.y = Mathf.Clamp(dragged.y + delta.y, -MaxPitch, MaxPitch);
                if (delta.sqrMagnitude > 0f) hint.Dismiss();
            }
            else Cursors.Set(this, CursorType.Grabbing);
            dragging = true;
            pointer = position;
        }
    }
}
