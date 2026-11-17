using UnityEngine;
using UnityEngine.UI;

namespace ProjectVita.Core
{
    /// <summary>
    /// UI 사각형을 평행사변형으로 기울인다 (각진 UI 느낌). 이미지 없이 단색 Image에 붙여서 쓴다.
    /// skew는 아래쪽과 위쪽의 가로 차이(px)다. 양수면 위쪽이 오른쪽으로 간다.
    /// 클릭 판정은 원래 사각형 그대로다.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class UISkew : BaseMeshEffect
    {
        [SerializeField] float skew = 24f;

        public float Skew
        {
            get => skew;
            set
            {
                skew = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            var rect = graphic.rectTransform.rect;
            if (rect.height <= 0f) return;

            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float t = (v.position.y - rect.yMin) / rect.height - 0.5f;
                v.position.x += t * skew;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
