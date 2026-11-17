using UnityEngine;
using UnityEngine.Rendering;

namespace CargoClash
{
    public sealed partial class RuinCourtyard
    {
        private Transform deliveryZone;
        private Vector2 deliveryHalfSize;
        private Material deliveryPaint;
        internal float DeliveryHeight => config.ruinDepotHeight;

        private void ConfigureDepot(LevelLayout level)
        {
            deliveryZone = level.truckZone;
            Vector3 centre = level.bedOrigin.position;
            centre.y = data.truckZone.y;
            Vector3 forward = Vector3.ProjectOnPlane(level.bedOrigin.forward, Vector3.up).normalized;
            deliveryZone.SetPositionAndRotation(centre, Quaternion.LookRotation(forward, Vector3.up));
            deliveryHalfSize = level.truckZoneHalfSize = data.truckZoneHalfSize;
            Transform previous = deliveryZone.Find("ZoneFloor");
            if (previous != null) previous.gameObject.SetActive(false);

            deliveryPaint = new Material(Load<Material>("RuinSurface")) { name = "Ruin depot warm safety paint" };
            deliveryPaint.SetTexture("_BaseMap", Texture2D.whiteTexture);
            deliveryPaint.SetColor("_BaseColor", new Color(217f/255f, 169f/255f, 40f/255f));
            deliveryPaint.SetFloat("_Smoothness", .18f);
            const float radius = .45f;
            var outline = new Vector3[36];
            for (int corner = 0; corner < 4; corner++)
            {
                float start = corner*90f;
                Vector2 offset = new Vector2(corner == 0 || corner == 3 ? 1 : -1,
                    corner < 2 ? 1 : -1);
                Vector2 pivot = Vector2.Scale(offset, deliveryHalfSize - Vector2.one*radius);
                for (int step = 0; step <= 8; step++)
                {
                    float angle = (start + step*90f/8f)*Mathf.Deg2Rad;
                    outline[corner*9+step] = new Vector3(pivot.x + Mathf.Cos(angle)*radius,
                        .035f, pivot.y + Mathf.Sin(angle)*radius);
                }
            }
            DepotLine("Cargo recovery boundary", outline, true, .12f);
            // Four inward arrows make the same perimeter readable from each approach.
            for (int side = 0; side < 4; side++)
            {
                Vector3 direction = Quaternion.Euler(0, side*90, 0)*Vector3.forward;
                float extent = side%2 == 0 ? deliveryHalfSize.y : deliveryHalfSize.x;
                Vector3 tip = direction*(extent-.55f) + Vector3.up*.04f;
                Vector3 tangent = Vector3.Cross(Vector3.up, direction)*.3f;
                DepotLine("Recovery arrow", new[] { tip+tangent+direction*.35f, tip, tip-tangent+direction*.35f }, false, .1f);
            }
        }

        private void DepotLine(string label, Vector3[] points, bool loop, float width)
        {
            var node = new GameObject(label);
            node.transform.SetParent(deliveryZone, false);
            LineRenderer line = node.AddComponent<LineRenderer>();
            line.sharedMaterial = deliveryPaint;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCornerVertices = 4;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
        }

        /// <summary>The cargo-bed footprint plus its walking margin, with a bounded vertical catch volume.</summary>
        internal bool ContainsDeliveryPoint(Vector3 point)
        {
            Vector3 p = deliveryZone.InverseTransformPoint(point);
            return Mathf.Abs(p.x) <= deliveryHalfSize.x && Mathf.Abs(p.z) <= deliveryHalfSize.y &&
                   p.y >= -.25f && p.y <= DeliveryHeight;
        }

        private void DisposeDepot()
        {
            if (deliveryPaint != null) Destroy(deliveryPaint);
        }
    }
}
