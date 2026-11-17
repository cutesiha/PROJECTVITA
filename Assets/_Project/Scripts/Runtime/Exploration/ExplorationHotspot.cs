using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectVita.Exploration
{
    /// <summary>
    /// 장소 화면의 클릭 영역 하나. 평소에는 보이지 않고, 마우스를 올리면 이름과 테두리가 나타난다.
    /// 개발 빌드에서는 위치를 맞추기 쉽게 항상 옅게 보인다.
    /// </summary>
    public sealed class ExplorationHotspot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] Image fill;
        [SerializeField] TMP_Text label;

        // UI가 선형 색공간에서 섞여서 낮은 알파도 진하게 보인다. 값을 작게 둔다.
        static readonly Color HoverColor = new Color(0.70f, 0.15f, 0.12f, 0.16f);
        static readonly Color DevColor = new Color(0.4f, 1f, 0.4f, 0.035f);

        public ExplorationData.ObjectDef Def { get; private set; }
        Action<ExplorationHotspot> onClick;
        bool hover;

        public void Setup(ExplorationData.ObjectDef def, Action<ExplorationHotspot> clicked)
        {
            Def = def;
            onClick = clicked;
            name = "Hotspot_" + def.id;
            var rt = (RectTransform)transform;
            // CSV 좌표는 왼쪽 위 기준이고, UI는 아래쪽이 +y라서 y를 뒤집는다.
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(def.area.x, -def.area.y);
            rt.sizeDelta = new Vector2(def.area.width, def.area.height);
            label.text = Debug.isDebugBuild ? $"{def.displayName}\n<size=60%>{def.id}</size>" : def.displayName;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hover = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hover = false;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) onClick?.Invoke(this);
        }

        void OnDisable()
        {
            hover = false;
            Refresh();
        }

        void Refresh()
        {
            if (fill == null) return;
            fill.color = hover ? HoverColor : Debug.isDebugBuild ? DevColor : Color.clear;
            label.gameObject.SetActive(hover || Debug.isDebugBuild);
        }
    }
}
