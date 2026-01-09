using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MindWeaveServer.BusinessLogic
{
    public static class JigsawPathGenerator
    {
        // Proporciones para una pestaña más elegante
        private const float TAB_LENGTH_RATIO = 0.20f;    // Qué tan lejos sale la pestaña
        private const float TAB_WIDTH_RATIO = 0.35f;     // Ancho de la parte bulbosa
        private const float NECK_WIDTH_RATIO = 0.15f;    // Ancho del cuello (más estrecho)
        private const float CURVE_SMOOTHNESS = 0.4f;     // Suavidad de las curvas

        public static GraphicsPath CreateJigsawPath(
            int baseWidth,
            int baseHeight,
            JigsawPieceEdges edges,
            int tabSize,
            out int offsetX,
            out int offsetY,
            out int totalWidth,
            out int totalHeight)
        {
            // Calcular offsets basados en las pestañas que sobresalen
            offsetX = edges.Left == JigsawEdgeType.Tab ? tabSize : 0;
            offsetY = edges.Top == JigsawEdgeType.Tab ? tabSize : 0;

            int rightExtra = edges.Right == JigsawEdgeType.Tab ? tabSize : 0;
            int bottomExtra = edges.Bottom == JigsawEdgeType.Tab ? tabSize : 0;

            totalWidth = baseWidth + offsetX + rightExtra;
            totalHeight = baseHeight + offsetY + bottomExtra;

            var path = new GraphicsPath();

            // Punto de inicio (esquina superior izquierda del área base)
            float startX = offsetX;
            float startY = offsetY;

            // Comenzar el path
            PointF currentPoint = new PointF(startX, startY);

            // Borde superior (izquierda a derecha)
            currentPoint = AddHorizontalEdge(path, currentPoint, baseWidth, edges.Top, tabSize, false);

            // Borde derecho (arriba a abajo)
            currentPoint = AddVerticalEdge(path, currentPoint, baseHeight, edges.Right, tabSize, false);

            // Borde inferior (derecha a izquierda)
            currentPoint = AddHorizontalEdge(path, currentPoint, baseWidth, edges.Bottom, tabSize, true);

            // Borde izquierdo (abajo a arriba)
            currentPoint = AddVerticalEdge(path, currentPoint, baseHeight, edges.Left, tabSize, true);

            path.CloseFigure();

            return path;
        }

        private static PointF AddHorizontalEdge(GraphicsPath path, PointF start, int length, JigsawEdgeType edgeType, int tabSize, bool reverse)
        {
            float direction = reverse ? -1 : 1;
            float endX = start.X + (length * direction);
            PointF end = new PointF(endX, start.Y);

            if (edgeType == JigsawEdgeType.Flat)
            {
                path.AddLine(start, end);
                return end;
            }

            // La dirección de la pestaña
            float tabDirection = edgeType == JigsawEdgeType.Tab ? -1 : 1;
            if (reverse) tabDirection *= -1;

            float tabDepth = tabSize * tabDirection;
            float midX = start.X + (length / 2f) * direction;

            // Dimensiones de la pestaña
            float neckWidth = length * NECK_WIDTH_RATIO;
            float tabWidth = length * TAB_WIDTH_RATIO;

            // Puntos clave
            float seg1End = start.X + (length * 0.35f) * direction;
            float seg2Start = start.X + (length * 0.65f) * direction;

            // Primer segmento recto
            PointF p1 = new PointF(seg1End, start.Y);
            path.AddLine(start, p1);

            // Curva de entrada al cuello
            PointF neckStart = new PointF(midX - (neckWidth / 2f) * direction, start.Y);
            PointF cp1 = new PointF(seg1End + (length * 0.05f) * direction, start.Y);
            PointF cp2 = new PointF(neckStart.X - (length * 0.02f) * direction, start.Y);
            path.AddBezier(p1, cp1, cp2, neckStart);

            // Curva del cuello hacia la parte bulbosa
            PointF bulbLeft = new PointF(midX - (tabWidth / 2f) * direction, start.Y + tabDepth * 0.7f);
            PointF cp3 = new PointF(neckStart.X, start.Y + tabDepth * 0.2f);
            PointF cp4 = new PointF(bulbLeft.X - (tabWidth * 0.1f) * direction, start.Y + tabDepth * 0.4f);
            path.AddBezier(neckStart, cp3, cp4, bulbLeft);

            // Curva de la parte bulbosa (semicírculo superior)
            PointF bulbTop = new PointF(midX, start.Y + tabDepth);
            PointF bulbRight = new PointF(midX + (tabWidth / 2f) * direction, start.Y + tabDepth * 0.7f);

            PointF cp5 = new PointF(bulbLeft.X + (tabWidth * 0.1f) * direction, start.Y + tabDepth * 1.1f);
            PointF cp6 = new PointF(bulbTop.X - (tabWidth * 0.25f) * direction, start.Y + tabDepth * 1.15f);
            path.AddBezier(bulbLeft, cp5, cp6, bulbTop);

            PointF cp7 = new PointF(bulbTop.X + (tabWidth * 0.25f) * direction, start.Y + tabDepth * 1.15f);
            PointF cp8 = new PointF(bulbRight.X - (tabWidth * 0.1f) * direction, start.Y + tabDepth * 1.1f);
            path.AddBezier(bulbTop, cp7, cp8, bulbRight);

            // Curva de la parte bulbosa hacia el cuello de salida
            PointF neckEnd = new PointF(midX + (neckWidth / 2f) * direction, start.Y);
            PointF cp9 = new PointF(bulbRight.X + (tabWidth * 0.1f) * direction, start.Y + tabDepth * 0.4f);
            PointF cp10 = new PointF(neckEnd.X, start.Y + tabDepth * 0.2f);
            path.AddBezier(bulbRight, cp9, cp10, neckEnd);

            // Curva de salida del cuello
            PointF p2 = new PointF(seg2Start, start.Y);
            PointF cp11 = new PointF(neckEnd.X + (length * 0.02f) * direction, start.Y);
            PointF cp12 = new PointF(seg2Start - (length * 0.05f) * direction, start.Y);
            path.AddBezier(neckEnd, cp11, cp12, p2);

            // Último segmento recto
            path.AddLine(p2, end);

            return end;
        }

        private static PointF AddVerticalEdge(GraphicsPath path, PointF start, int length, JigsawEdgeType edgeType, int tabSize, bool reverse)
        {
            float direction = reverse ? -1 : 1;
            float endY = start.Y + (length * direction);
            PointF end = new PointF(start.X, endY);

            if (edgeType == JigsawEdgeType.Flat)
            {
                path.AddLine(start, end);
                return end;
            }

            float tabDirection = edgeType == JigsawEdgeType.Tab ? 1 : -1;
            if (reverse) tabDirection *= -1;

            float tabDepth = tabSize * tabDirection;
            float midY = start.Y + (length / 2f) * direction;

            float neckWidth = length * NECK_WIDTH_RATIO;
            float tabWidth = length * TAB_WIDTH_RATIO;

            float seg1End = start.Y + (length * 0.35f) * direction;
            float seg2Start = start.Y + (length * 0.65f) * direction;

            // Primer segmento recto
            PointF p1 = new PointF(start.X, seg1End);
            path.AddLine(start, p1);

            // Curva de entrada al cuello
            PointF neckStart = new PointF(start.X, midY - (neckWidth / 2f) * direction);
            PointF cp1 = new PointF(start.X, seg1End + (length * 0.05f) * direction);
            PointF cp2 = new PointF(start.X, neckStart.Y - (length * 0.02f) * direction);
            path.AddBezier(p1, cp1, cp2, neckStart);

            // Curva del cuello hacia la parte bulbosa
            PointF bulbTop = new PointF(start.X + tabDepth * 0.7f, midY - (tabWidth / 2f) * direction);
            PointF cp3 = new PointF(start.X + tabDepth * 0.2f, neckStart.Y);
            PointF cp4 = new PointF(start.X + tabDepth * 0.4f, bulbTop.Y - (tabWidth * 0.1f) * direction);
            path.AddBezier(neckStart, cp3, cp4, bulbTop);

            // Curva de la parte bulbosa
            PointF bulbMid = new PointF(start.X + tabDepth, midY);
            PointF bulbBottom = new PointF(start.X + tabDepth * 0.7f, midY + (tabWidth / 2f) * direction);

            PointF cp5 = new PointF(start.X + tabDepth * 1.1f, bulbTop.Y + (tabWidth * 0.1f) * direction);
            PointF cp6 = new PointF(start.X + tabDepth * 1.15f, bulbMid.Y - (tabWidth * 0.25f) * direction);
            path.AddBezier(bulbTop, cp5, cp6, bulbMid);

            PointF cp7 = new PointF(start.X + tabDepth * 1.15f, bulbMid.Y + (tabWidth * 0.25f) * direction);
            PointF cp8 = new PointF(start.X + tabDepth * 1.1f, bulbBottom.Y - (tabWidth * 0.1f) * direction);
            path.AddBezier(bulbMid, cp7, cp8, bulbBottom);

            // Curva de la parte bulbosa hacia el cuello de salida
            PointF neckEnd = new PointF(start.X, midY + (neckWidth / 2f) * direction);
            PointF cp9 = new PointF(start.X + tabDepth * 0.4f, bulbBottom.Y + (tabWidth * 0.1f) * direction);
            PointF cp10 = new PointF(start.X + tabDepth * 0.2f, neckEnd.Y);
            path.AddBezier(bulbBottom, cp9, cp10, neckEnd);

            // Curva de salida del cuello
            PointF p2 = new PointF(start.X, seg2Start);
            PointF cp11 = new PointF(start.X, neckEnd.Y + (length * 0.02f) * direction);
            PointF cp12 = new PointF(start.X, seg2Start - (length * 0.05f) * direction);
            path.AddBezier(neckEnd, cp11, cp12, p2);

            // Último segmento recto
            path.AddLine(p2, end);

            return end;
        }
    }
}