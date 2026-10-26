using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.UI
{
    /// <summary>
    /// Line icons drawn in code, so no image files need importing. Each icon is described on a
    /// 24 x 24 grid with y pointing down (the same coordinates as the SVG mockups in the plan), then
    /// rasterised once into a white, anti-aliased sprite that the UI tints.
    /// </summary>
    public static class AuraIcons
    {
        public const string Play = "play", Interactive = "interactive", Note = "note", Plain = "plain",
                            Checklist = "checklist", Shapes = "shapes", Cube = "cube", Sphere = "sphere",
                            Cylinder = "cylinder", Capsule = "capsule", Plane = "plane", Scan = "scan",
                            Library = "library", Join = "join", Back = "back", Forward = "forward",
                            Ok = "ok", House = "house", Engine = "engine", Light = "light", Orbit = "orbit",
                            Still = "still", Backspace = "backspace", Paste = "paste", Trash = "trash",
                            Edit = "edit", Plus = "plus", Camera = "camera", User = "user",
                            Reports = "reports", Open = "open",
                            // 3D viewer
                            Parts = "parts", Eye = "eye", EyeOff = "eyeoff", Chart = "chart", Room = "room",
                            Simulate = "simulate", Section = "section", AI = "ai", Puzzle = "puzzle",
                            Record = "record", Prev = "prev", Next = "next", PlayFill = "playfill",
                            Pause = "pause", Stop = "stop", Search = "search", Send = "send", Mic = "mic",
                            MicOff = "micoff", Ear = "ear", EarOff = "earoff", Flip = "flip", Fill = "fill",
                            Wind = "wind", Sun = "sun", Fire = "fire", Quiz = "quiz", Target = "target",
                            Doc = "doc", Link = "link", Crown = "crown", Star = "star", Follow = "follow",
                            Close = "close", More = "more", Explode = "explode", Speed = "speed",
                            Copy = "copy", Hint = "hint", Trophy = "trophy";

        private const int   TexSize = 128;
        private const float Scale   = TexSize / 24f;

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        /// <summary>The sprite for an icon name, or null for an unknown name.</summary>
        public static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out Sprite s)) return s;

            var p = new Painter();
            if (!Describe(name, p)) return null;

            s = p.ToSprite("AuraIcon_" + name);
            _cache[name] = s;
            return s;
        }

        /// <summary>A sensible picture for a model when no rendered thumbnail exists yet.</summary>
        public static string ForModelName(string modelName, bool isScan)
        {
            string n = (modelName ?? "").ToLowerInvariant();
            if (n.Contains("house") || n.Contains("home") || n.Contains("building")) return House;
            if (n.Contains("engine") || n.Contains("turbine") || n.Contains("motor")) return Engine;
            return isScan ? Scan : Cube;
        }

        private static bool Describe(string name, Painter p)
        {
            switch (name)
            {
                case Play:
                    p.Closed(4, 7, 12, 3, 20, 7, 12, 11);
                    p.Poly(4, 7, 4, 17, 12, 21, 20, 17, 20, 7);
                    p.Line(12, 11, 12, 21);
                    return true;
                case Interactive:
                    p.Circle(12, 12, 3);
                    p.Ellipse(12, 12, 10, 4, 0);
                    p.Ellipse(12, 12, 10, 4, 60);
                    p.Ellipse(12, 12, 10, 4, 120);
                    return true;
                case Note:
                    p.Closed(5, 3, 15, 3, 19, 7, 19, 21, 5, 21);
                    p.Poly(15, 3, 15, 7, 19, 7);
                    p.Line(8, 12, 16, 12);
                    p.Line(8, 16, 13, 16);
                    return true;
                case Plain:
                    p.Line(5, 6, 19, 6); p.Line(5, 10, 19, 10); p.Line(5, 14, 19, 14); p.Line(5, 18, 14, 18);
                    return true;
                case Checklist:
                    p.RoundRect(3.5f, 4, 5, 5, 1.2f);
                    p.Poly(4.8f, 6.6f, 6.1f, 7.8f, 8.1f, 5.4f);
                    p.RoundRect(3.5f, 14, 5, 5, 1.2f);
                    p.Line(11.5f, 6.5f, 20, 6.5f);
                    p.Line(11.5f, 16.5f, 20, 16.5f);
                    return true;
                case Shapes:
                    p.Circle(7.5f, 7.5f, 4);
                    p.RoundRect(13, 13, 8, 8, 1);
                    p.Closed(17, 3, 21, 10, 13, 10);
                    return true;
                case Cube:
                    p.Closed(12, 3, 20, 7.5f, 20, 16.5f, 12, 21, 4, 16.5f, 4, 7.5f);
                    p.Poly(4, 7.5f, 12, 12, 20, 7.5f);
                    p.Line(12, 12, 12, 21);
                    return true;
                case Sphere:
                    p.Circle(12, 12, 8.5f);
                    p.Ellipse(12, 12, 8.5f, 3.2f, 0);
                    return true;
                case Cylinder:
                    p.Ellipse(12, 5.5f, 7, 2.5f, 0);
                    p.Line(5, 5.5f, 5, 18.5f);
                    p.Line(19, 5.5f, 19, 18.5f);
                    p.EllipseArc(12, 18.5f, 7, 2.5f, 0, 180);
                    return true;
                case Capsule:
                    p.RoundRect(7, 2.5f, 10, 19, 5);
                    p.EllipseArc(12, 12, 5, 2.2f, 0, 180);
                    return true;
                case Plane:
                    p.Closed(2.5f, 15, 9, 8, 21.5f, 8, 15, 15);
                    return true;
                case Scan:
                    p.Poly(3, 8, 3, 3, 8, 3); p.Poly(16, 3, 21, 3, 21, 8);
                    p.Poly(21, 16, 21, 21, 16, 21); p.Poly(8, 21, 3, 21, 3, 16);
                    p.Closed(12, 7.5f, 16, 9.7f, 16, 14.3f, 12, 16.5f, 8, 14.3f, 8, 9.7f);
                    return true;
                case Library:
                    p.RoundRect(3, 3, 7.5f, 7.5f, 1.8f); p.RoundRect(13.5f, 3, 7.5f, 7.5f, 1.8f);
                    p.RoundRect(3, 13.5f, 7.5f, 7.5f, 1.8f); p.RoundRect(13.5f, 13.5f, 7.5f, 7.5f, 1.8f);
                    return true;
                case Join:
                    p.Circle(9, 8, 3.2f);
                    p.EllipseArc(9, 19.5f, 6, 5.5f, 180, 360);
                    p.Line(18, 8, 18, 14); p.Line(15, 11, 21, 11);
                    return true;
                case Back:
                    p.Poly(15, 5, 8, 12, 15, 19);
                    return true;
                case Forward:
                    p.Poly(9, 5, 16, 12, 9, 19);
                    return true;
                case Ok:
                    p.Poly(5, 12.5f, 9.5f, 17, 19, 7.5f);
                    return true;
                case House:
                    p.Poly(3, 11, 12, 4, 21, 11);
                    p.Poly(5.5f, 9.5f, 5.5f, 20, 18.5f, 20, 18.5f, 9.5f);
                    p.Poly(10, 20, 10, 15, 14, 15, 14, 20);
                    return true;
                case Engine:
                    p.Circle(12, 12, 8.5f);
                    p.Circle(12, 12, 2);
                    p.Line(12, 3.5f, 12, 10); p.Line(12, 14, 12, 20.5f);
                    p.Line(3.5f, 12, 10, 12); p.Line(14, 12, 20.5f, 12);
                    p.Line(6, 6, 10.6f, 10.6f); p.Line(13.4f, 13.4f, 18, 18);
                    p.Line(18, 6, 13.4f, 10.6f); p.Line(10.6f, 13.4f, 6, 18);
                    return true;
                case Light:
                    p.Circle(12, 12, 4);
                    p.Line(12, 2.5f, 12, 4.5f); p.Line(12, 19.5f, 12, 21.5f);
                    p.Line(2.5f, 12, 4.5f, 12); p.Line(19.5f, 12, 21.5f, 12);
                    p.Line(5.3f, 5.3f, 6.7f, 6.7f); p.Line(17.3f, 17.3f, 18.7f, 18.7f);
                    p.Line(5.3f, 18.7f, 6.7f, 17.3f); p.Line(17.3f, 6.7f, 18.7f, 5.3f);
                    return true;
                case Orbit:
                    p.Arc(12, 12, 8, 0, 309);
                    p.Poly(17, 2.5f, 17, 6.1f, 13.4f, 6.1f);
                    p.Circle(12, 12, 2);
                    return true;
                case Still:
                    p.RoundRect(4, 13, 16, 7, 1.5f);
                    p.Poly(8, 13, 8, 8, 12, 4, 16, 8, 16, 13);
                    return true;
                case Backspace:
                    p.Closed(8, 5, 21, 5, 21, 19, 8, 19, 2, 12);
                    p.Line(12, 9.5f, 17, 14.5f); p.Line(17, 9.5f, 12, 14.5f);
                    return true;
                case Paste:
                    p.RoundRect(5, 4, 14, 17, 2);
                    p.RoundRect(9, 2.5f, 6, 3.5f, 1);
                    p.Line(8.5f, 11, 15.5f, 11); p.Line(8.5f, 15, 13, 15);
                    return true;
                case Trash:
                    p.Line(4, 6.5f, 20, 6.5f);
                    p.Poly(9, 6.5f, 9, 4, 15, 4, 15, 6.5f);
                    p.Poly(6, 6.5f, 7, 20, 17, 20, 18, 6.5f);
                    p.Line(10, 10, 10, 16.5f); p.Line(14, 10, 14, 16.5f);
                    return true;
                case Edit:
                    p.Closed(4, 20, 4.5f, 16, 15.5f, 5, 19, 8.5f, 8, 19.5f);
                    p.Line(13.5f, 7, 17, 10.5f);
                    return true;
                case Plus:
                    p.Line(12, 5, 12, 19); p.Line(5, 12, 19, 12);
                    return true;
                case Camera:
                    p.RoundRect(3, 7, 18, 13, 2.5f);
                    p.Poly(8, 7, 9.5f, 4.5f, 14.5f, 4.5f, 16, 7);
                    p.Circle(12, 13.5f, 3.5f);
                    return true;
                case User:
                    p.Circle(12, 8, 3.6f);
                    p.EllipseArc(12, 20.5f, 6.5f, 6, 180, 360);
                    return true;
                case Reports:
                    p.Line(4, 20, 20, 20);
                    p.Line(7, 17, 7, 11); p.Line(12, 17, 12, 6); p.Line(17, 17, 17, 13);
                    return true;
                case Open:
                    p.Poly(13, 4, 20, 4, 20, 11);
                    p.Line(20, 4, 11, 13);
                    p.Poly(17, 14, 17, 20, 4, 20, 4, 7, 10, 7);
                    return true;

                // ── 3D viewer ──
                case Parts:
                    p.Closed(12, 3, 21, 8, 12, 13, 3, 8);
                    p.Poly(3, 12.5f, 12, 17.5f, 21, 12.5f);
                    p.Poly(3, 16.5f, 12, 21.5f, 21, 16.5f);
                    return true;
                case Eye:
                    p.EllipseArc(12, 12, 10, 6.5f, 180, 360);
                    p.EllipseArc(12, 12, 10, 6.5f, 0, 180);
                    p.Circle(12, 12, 3);
                    return true;
                case EyeOff:
                    p.EllipseArc(12, 12, 10, 6.5f, 180, 360);
                    p.EllipseArc(12, 12, 10, 6.5f, 0, 180);
                    p.Line(4, 20, 20, 4);
                    return true;
                case Chart:
                    p.Line(4, 20, 20, 20);
                    p.Line(7, 16, 7, 11); p.Line(12, 16, 12, 5); p.Line(17, 16, 17, 9);
                    return true;
                case Room:
                    p.Circle(9, 8, 3.2f);
                    p.EllipseArc(9, 19.5f, 6, 5.5f, 180, 360);
                    p.Circle(17, 9, 2.5f);
                    p.EllipseArc(17.5f, 19, 4, 4.5f, 210, 360);
                    return true;
                case Simulate:
                    p.Circle(12, 12, 9);
                    p.FillPoly(10, 8, 16, 12, 10, 16);
                    return true;
                case Section:
                    p.Closed(4, 8, 12, 4, 20, 8, 20, 16, 12, 20, 4, 16);
                    p.Line(13, 2.5f, 13, 5); p.Line(13, 8, 13, 11); p.Line(13, 14, 13, 17); p.Line(13, 19.5f, 13, 21.5f);
                    return true;
                case AI:
                    p.Closed(12, 3, 13.9f, 8.1f, 19, 10, 13.9f, 11.9f, 12, 17, 10.1f, 11.9f, 5, 10, 10.1f, 8.1f);
                    p.Closed(18, 15, 18.8f, 17.2f, 21, 18, 18.8f, 18.8f, 18, 21, 17.2f, 18.8f, 15, 18, 17.2f, 17.2f);
                    return true;
                case Puzzle:
                    p.Poly(4, 8, 8.5f, 8);
                    p.EllipseArc(10, 7.5f, 2, 2, 165, 375);
                    p.Poly(11.5f, 8, 16, 8, 16, 12);
                    p.EllipseArc(16.5f, 14, 2, 2, 255, 465);
                    p.Poly(16, 16, 16, 20, 4, 20, 4, 8);
                    return true;
                case Record:
                    p.Circle(12, 12, 8.5f);
                    p.Disc(12, 12, 4.5f);
                    return true;
                case Prev:
                    p.FillPoly(19, 5, 9, 12, 19, 19);
                    p.FillPoly(5, 5, 7.5f, 5, 7.5f, 19, 5, 19);
                    return true;
                case Next:
                    p.FillPoly(5, 5, 15, 12, 5, 19);
                    p.FillPoly(16.5f, 5, 19, 5, 19, 19, 16.5f, 19);
                    return true;
                case PlayFill:
                    p.FillPoly(7, 4.5f, 19.5f, 12, 7, 19.5f);
                    return true;
                case Pause:
                    p.FillPoly(6, 5, 10, 5, 10, 19, 6, 19);
                    p.FillPoly(14, 5, 18, 5, 18, 19, 14, 19);
                    return true;
                case Stop:
                    p.FillPoly(6, 6, 18, 6, 18, 18, 6, 18);
                    return true;
                case Search:
                    p.Circle(10.5f, 10.5f, 6);
                    p.Line(15, 15, 20.5f, 20.5f);
                    return true;
                case Send:
                    p.Line(5, 12, 19, 12);
                    p.Poly(13, 6, 19, 12, 13, 18);
                    return true;
                case Mic:
                    p.RoundRect(9, 3, 6, 11, 3);
                    p.EllipseArc(12, 11, 7, 7, 0, 180);
                    p.Line(12, 18, 12, 21);
                    return true;
                case MicOff:
                    p.RoundRect(9, 3, 6, 11, 3);
                    p.EllipseArc(12, 11, 7, 7, 0, 180);
                    p.Line(12, 18, 12, 21);
                    p.Line(4, 3, 20, 21);
                    return true;
                case Ear:
                    p.EllipseArc(12, 12, 9, 9, 180, 360);
                    p.RoundRect(3, 12, 4, 8, 1.5f);
                    p.RoundRect(17, 12, 4, 8, 1.5f);
                    return true;
                case EarOff:
                    p.EllipseArc(12, 12, 9, 9, 180, 360);
                    p.RoundRect(3, 12, 4, 8, 1.5f);
                    p.RoundRect(17, 12, 4, 8, 1.5f);
                    p.Line(3, 3, 21, 21);
                    return true;
                case Flip:
                    p.Poly(7, 4, 4, 7, 7, 10);
                    p.Line(4, 7, 16, 7);
                    p.EllipseArc(16, 11, 4, 4, 270, 450);
                    p.Line(16, 15, 14, 15);
                    p.Poly(17, 20, 20, 17, 17, 14);
                    p.Line(20, 17, 8, 17);
                    return true;
                case Fill:
                    p.RoundRect(4, 4, 16, 16, 3);
                    p.Line(4, 14, 14, 4); p.Line(8, 20, 20, 8); p.Line(14, 20, 20, 14);
                    return true;
                case Wind:
                    p.Line(3, 9, 14, 9);
                    p.EllipseArc(14, 6, 3, 3, 90, 360);
                    p.Line(3, 15, 17, 15);
                    p.EllipseArc(17, 18, 3, 3, -90, 180);
                    return true;
                case Sun:
                    return Describe(Light, p);
                case Fire:
                    p.Poly(12, 3, 13.5f, 7, 17, 10.5f, 18, 14);
                    p.EllipseArc(12, 14.5f, 6, 6.5f, 0, 180);
                    p.Poly(6, 14, 7, 10.5f, 9, 8, 10, 11, 12, 3);
                    return true;
                case Quiz:
                    p.Circle(12, 12, 9);
                    p.EllipseArc(12, 9.5f, 2.6f, 2.6f, 180, 400);
                    p.Line(12.5f, 12, 12, 13.8f);
                    p.Disc(12, 17, 1.1f);
                    return true;
                case Target:
                    p.Circle(12, 12, 8.5f);
                    p.Circle(12, 12, 4.5f);
                    p.Disc(12, 12, 1.4f);
                    return true;
                case Doc:
                    p.Closed(6, 3, 14, 3, 18, 7, 18, 21, 6, 21);
                    p.Poly(14, 3, 14, 7, 18, 7);
                    p.Line(9, 13, 15, 13); p.Line(9, 17, 13, 17);
                    return true;
                case Link:
                    p.RoundRect(2.5f, 9, 10, 6, 3);
                    p.RoundRect(11.5f, 9, 10, 6, 3);
                    return true;
                case Crown:
                    p.Closed(3, 8, 7.5f, 12, 12, 5, 16.5f, 12, 21, 8, 19, 19, 5, 19);
                    return true;
                case Star:
                    p.FillPoly(12, 2.5f, 14.8f, 8.6f, 21.3f, 9.3f, 16.4f, 13.7f, 17.8f, 20.2f, 12, 16.9f, 6.2f, 20.2f, 7.6f, 13.7f, 2.7f, 9.3f, 9.2f, 8.6f);
                    return true;
                case Follow:
                    p.Circle(12, 12, 3);
                    p.Circle(12, 12, 8);
                    p.Line(12, 1.5f, 12, 4); p.Line(12, 20, 12, 22.5f); p.Line(1.5f, 12, 4, 12); p.Line(20, 12, 22.5f, 12);
                    return true;
                case Close:
                    p.Line(6, 6, 18, 18); p.Line(18, 6, 6, 18);
                    return true;
                case More:
                    p.Disc(5, 12, 1.8f); p.Disc(12, 12, 1.8f); p.Disc(19, 12, 1.8f);
                    return true;
                case Explode:
                    p.RoundRect(9, 9, 6, 6, 1);
                    p.Poly(3, 7, 3, 3, 7, 3); p.Line(3, 3, 7, 7);
                    p.Poly(17, 3, 21, 3, 21, 7); p.Line(21, 3, 17, 7);
                    p.Poly(21, 17, 21, 21, 17, 21); p.Line(21, 21, 17, 17);
                    p.Poly(7, 21, 3, 21, 3, 17); p.Line(3, 21, 7, 17);
                    return true;
                case Speed:
                    p.EllipseArc(12, 15, 9, 9, 180, 360);
                    p.Line(12, 15, 16.5f, 9);
                    p.Disc(12, 15, 1.5f);
                    return true;
                case Copy:
                    p.RoundRect(8, 8, 12, 13, 2);
                    p.Poly(5, 16, 4, 16, 4, 4, 15, 4, 15, 5);
                    return true;
                case Hint:
                    p.EllipseArc(12, 9.5f, 6, 6, 140, 400);
                    p.Poly(8.5f, 14, 9.5f, 17, 14.5f, 17, 15.5f, 14);
                    p.Line(10, 20, 14, 20);
                    return true;
                case Trophy:
                    p.Poly(7, 4, 17, 4, 17, 9);
                    p.EllipseArc(12, 9, 5, 5.5f, 0, 180);
                    p.Line(7, 4, 7, 9);
                    p.EllipseArc(7, 7.5f, 2.5f, 2.5f, 90, 270);
                    p.EllipseArc(17, 7.5f, 2.5f, 2.5f, -90, 90);
                    p.Line(12, 14.5f, 12, 18); p.Line(8, 20, 16, 20);
                    return true;
            }
            return false;
        }

        /// <summary>Collects stroked segments, then rasterises them by distance to each pixel.</summary>
        private class Painter
        {
            private const float Stroke = 1.8f;
            private readonly List<Vector4> _segs = new List<Vector4>();
            private readonly List<Vector2[]> _fills = new List<Vector2[]>();

            /// <summary>A filled polygon (play, pause, star...).</summary>
            public void FillPoly(params float[] xy)
            {
                var pts = new Vector2[xy.Length / 2];
                for (int i = 0; i < pts.Length; i++) pts[i] = P(xy[i * 2], xy[i * 2 + 1]);
                _fills.Add(pts);
            }

            /// <summary>A filled dot.</summary>
            public void Disc(float cx, float cy, float r)
            {
                var pts = new Vector2[24];
                for (int i = 0; i < 24; i++)
                {
                    float t = i / 24f * Mathf.PI * 2f;
                    pts[i] = P(cx + r * Mathf.Cos(t), cy + r * Mathf.Sin(t));
                }
                _fills.Add(pts);
            }

            private static float FillCoverage(Vector2 q, Vector2[] poly)
            {
                bool inside = false;
                float edge = float.MaxValue;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    Vector2 a = poly[i], b = poly[j];
                    if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    float d = DistToSegment(q, a, b);
                    if (d < edge) edge = d;
                }
                return inside ? Mathf.Clamp01(0.5f + edge) : Mathf.Clamp01(0.5f - edge);
            }

            // Converts SVG-style coordinates (y down) into texture pixels (y up).
            private static Vector2 P(float x, float y) => new Vector2(x * Scale, (24f - y) * Scale);

            private void Seg(Vector2 a, Vector2 b) => _segs.Add(new Vector4(a.x, a.y, b.x, b.y));

            public void Line(float x1, float y1, float x2, float y2) => Seg(P(x1, y1), P(x2, y2));

            public void Poly(params float[] xy)
            {
                for (int i = 0; i + 3 < xy.Length; i += 2) Line(xy[i], xy[i + 1], xy[i + 2], xy[i + 3]);
            }

            public void Closed(params float[] xy)
            {
                Poly(xy);
                int n = xy.Length;
                if (n >= 4) Line(xy[n - 2], xy[n - 1], xy[0], xy[1]);
            }

            public void Circle(float cx, float cy, float r) => Ellipse(cx, cy, r, r, 0);

            public void Arc(float cx, float cy, float r, float fromDeg, float toDeg) =>
                EllipseArc(cx, cy, r, r, fromDeg, toDeg);

            public void Ellipse(float cx, float cy, float rx, float ry, float rotDeg) =>
                EllipseArc(cx, cy, rx, ry, 0, 360, rotDeg);

            /// <summary>Angles in SVG convention: 0 = +x, 90 = down.</summary>
            public void EllipseArc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg, float rotDeg = 0)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 7.5f));
                float rot = rotDeg * Mathf.Deg2Rad, cr = Mathf.Cos(rot), sr = Mathf.Sin(rot);
                Vector2 prev = Vector2.zero;
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                    float ex = rx * Mathf.Cos(t), ey = ry * Mathf.Sin(t);
                    Vector2 cur = P(cx + ex * cr - ey * sr, cy + ex * sr + ey * cr);
                    if (i > 0) Seg(prev, cur);
                    prev = cur;
                }
            }

            public void RoundRect(float x, float y, float w, float h, float r)
            {
                r = Mathf.Min(r, Mathf.Min(w, h) / 2f);
                Line(x + r, y, x + w - r, y);
                Line(x + w, y + r, x + w, y + h - r);
                Line(x + w - r, y + h, x + r, y + h);
                Line(x, y + h - r, x, y + r);
                EllipseArc(x + w - r, y + r, r, r, 270, 360);
                EllipseArc(x + w - r, y + h - r, r, r, 0, 90);
                EllipseArc(x + r, y + h - r, r, r, 90, 180);
                EllipseArc(x + r, y + r, r, r, 180, 270);
            }

            public Sprite ToSprite(string name)
            {
                var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, true)
                {
                    name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                    hideFlags = HideFlags.DontSave
                };

                float halfW = Stroke * Scale * 0.5f;
                var px = new Color32[TexSize * TexSize];

                for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    var q = new Vector2(x + 0.5f, y + 0.5f);
                    float best = float.MaxValue;
                    for (int i = 0; i < _segs.Count; i++)
                    {
                        Vector4 s = _segs[i];
                        float d = DistToSegment(q, new Vector2(s.x, s.y), new Vector2(s.z, s.w));
                        if (d < best) best = d;
                    }
                    float a = Mathf.Clamp01(halfW + 0.5f - best);
                    for (int f = 0; f < _fills.Count && a < 1f; f++) a = Mathf.Max(a, FillCoverage(q, _fills[f]));
                    px[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

                tex.SetPixels32(px);
                tex.Apply(true, true);
                return Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), 100f);
            }

            private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
            {
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                return (p - (a + ab * t)).magnitude;
            }
        }
    }
}
