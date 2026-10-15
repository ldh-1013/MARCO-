using UnityEngine;

// Keeps the built-in dynamic font atlas bound to depth-tested world labels after scene reloads.
[ExecuteAlways]
[RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
public sealed class WorldLabelFontBinding : MonoBehaviour
{
    TextMesh textMesh;
    MeshRenderer meshRenderer;
    MaterialPropertyBlock properties;

    void OnEnable()
    {
        textMesh = GetComponent<TextMesh>();
        meshRenderer = GetComponent<MeshRenderer>();
        Font.textureRebuilt += OnFontRebuilt;
        if (textMesh.font != null)
            textMesh.font.RequestCharactersInTexture(textMesh.text, textMesh.fontSize, textMesh.fontStyle);
        BindAtlas();
    }

    void OnDisable() => Font.textureRebuilt -= OnFontRebuilt;

    void OnFontRebuilt(Font font)
    {
        if (textMesh != null && textMesh.font == font) BindAtlas();
    }

    void BindAtlas()
    {
        if (textMesh == null || meshRenderer == null || textMesh.font == null) return;
        properties ??= new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(properties);
        properties.SetTexture("_MainTex", textMesh.font.material.mainTexture);
        meshRenderer.SetPropertyBlock(properties);
    }
}
