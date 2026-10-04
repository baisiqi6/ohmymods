using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OhMyMods.AndroidProbe;

// Owns the small native UGUI hit surface for the IMGUI float ball/panel (Issue #119).
//
// Root cause: menu pointer input runs through the game's own UGUI EventSystem
// (RewiredStandaloneInputModule), which cannot see the mod's IMGUI float ball, so a
// single touch reaches both layers. Two raycast-blocking transparent Images, sized to
// the same rectangles as the IMGUI ball/panel, consume the pointer before the native
// menu does.
//
// The Images are hit areas only: raycastTarget=true, fully clear color, no sprite and
// no alpha mask. Geometry is read from FloatLayout, not recomputed: the ball uses
// HitBall's square (X-TouchSize/2, Y-TouchSize/2, TouchSize, TouchSize), the panel
// uses (PanelX, PanelY, PanelWidth, PanelHeight), both in native screen pixels.
// Screen top-left coordinates become UGUI top-left-anchored anchoredPosition (x, -y).
//
// Objects are created with GameObject(string, Type[]) carrying RectTransform, so the
// rect exists from construction. The interop strips type attributes, so Image/Canvas
// RequireComponent auto-provision is not observable there; the constructor is the
// actual observed API instead of a GetComponent guess. Creation is transactional: the
// root is deactivated before any component is added, a failing step deactivates then
// destroys the owned object before rethrowing, and the root field takes ownership only
// after the object is fully built. Whether the surface actually wins the native menu's
// layer/depth ordering is a device question.
internal sealed class MobileUiInputSurface
{
    private GameObject root;
    private GameObject ballObject;
    private GameObject panelObject;
    private RectTransform ballTransform;
    private RectTransform panelTransform;
    private Rect ballRect;
    private Rect panelRect;

    internal void Sync(GameObject owner, FloatLayout layout, bool ready)
    {
        if (!ready || layout == null) { Hide(); return; }
        if (root == null) Create(owner);
        SyncRect(ballTransform, layout.X - layout.TouchSize / 2, layout.Y - layout.TouchSize / 2,
            layout.TouchSize, layout.TouchSize, ref ballRect);
        SyncRect(panelTransform, layout.PanelX, layout.PanelY,
            layout.PanelWidth, layout.PanelHeight, ref panelRect);
        SetActive(panelObject, layout.Expanded);
        // The first activation happens after the rects are written (the root is created
        // inactive). Later frames update the already-active root only when geometry
        // actually changed.
        SetActive(root, true);
    }

    internal void Hide()
    {
        SetActive(root, false);
    }

    // Destroy only what this class created.
    internal void Dispose()
    {
        SetActive(root, false);
        if (root == null) return;
        UnityEngine.Object.Destroy(root);
        root = null;
        ballObject = null;
        panelObject = null;
        ballTransform = null;
        panelTransform = null;
        ballRect = default;
        panelRect = default;
    }

    private void Create(GameObject owner)
    {
        GameObject created = null;
        try
        {
            created = NewRectObject("OhMyMods.UiInputSurface");
            created.SetActive(false);
            created.transform.SetParent(owner.transform, false);
            created.layer = owner.layer;
            var canvas = created.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;
            created.AddComponent<GraphicRaycaster>();
            // The ball is created second so it stays later in the child order (on top).
            panelObject = AddImage(created, "OhMyMods.UiInputSurface.Panel");
            panelTransform = panelObject.GetComponent<RectTransform>();
            ballObject = AddImage(created, "OhMyMods.UiInputSurface.Ball");
            ballTransform = ballObject.GetComponent<RectTransform>();
            root = created;
        }
        catch
        {
            if (created != null)
            {
                created.SetActive(false);
                UnityEngine.Object.Destroy(created);
            }
            throw;
        }
    }

    private static GameObject AddImage(GameObject parent, string name)
    {
        var go = NewRectObject(name);
        try
        {
            // Parented to the owned inactive root before any component is added, so a
            // failing step cannot leave an active raycast surface at the scene root.
            go.transform.SetParent(parent.transform, false);
            go.layer = parent.layer;
            var image = go.AddComponent<Image>();
            image.raycastTarget = true;
            image.color = new Color(0f, 0f, 0f, 0f);
            return go;
        }
        catch
        {
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
            throw;
        }
    }

    private static GameObject NewRectObject(string name)
    {
        return new GameObject(name, new Il2CppSystem.Type[] { Il2CppType.From(typeof(RectTransform)) });
    }

    private void SyncRect(RectTransform rect, float x, float y, float width, float height, ref Rect current)
    {
        var target = new Rect(x, y, width, height);
        if (current == target) return;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(target.x, -target.y);
        rect.sizeDelta = new Vector2(target.width, target.height);
        current = target;
    }

    private static void SetActive(GameObject go, bool value)
    {
        if (go != null && go.activeSelf != value) go.SetActive(value);
    }
}
