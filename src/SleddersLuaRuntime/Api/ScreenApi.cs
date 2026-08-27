using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class ScreenApi
    {
        private static Type? _guiType;
        private static Type? _rectType;
        private static Type? _colorType;
        private static Type? _texture2DType;
        private static Type? _eventType;
        private static Type? _guiUtilityType;
        private static Type? _vector2Type;
        private static ConstructorInfo? _rectCtor;
        private static ConstructorInfo? _colorCtor;
        private static ConstructorInfo? _vector2Ctor;
        private static PropertyInfo? _guiColor;
        private static PropertyInfo? _whiteTexture;
        private static PropertyInfo? _eventCurrent;
        private static PropertyInfo? _eventKind;
        private static PropertyInfo? _guiMatrix;
        private static MethodInfo? _rotateAroundPivot;
        private static MethodInfo? _drawTexture;
        private static MethodInfo? _label;
        private static MethodInfo? _box;

        public static Table Build(LuaModInstance mod)
        {
            Ensure();
            var table = new Table(mod.Script);

            table.Set("setColor", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                ReadColor(args, offset, out double r, out double g, out double b, out double a);
                mod.SetDrawColor(r, g, b, a);
                return DynValue.True;
            }));
            table.Set("resetColor", DynValue.NewCallback((ctx, args) =>
            {
                mod.ResetDrawColor();
                return DynValue.True;
            }));

            table.Set("rectangle", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double x = RequireNumber(args, offset, "screen.rectangle(x, y, w, h)");
                double y = RequireNumber(args, offset + 1, "screen.rectangle(x, y, w, h)");
                double w = RequireNumber(args, offset + 2, "screen.rectangle(x, y, w, h)");
                double h = RequireNumber(args, offset + 3, "screen.rectangle(x, y, w, h)");
                return DynValue.NewBoolean(DrawRectangle(mod, x, y, w, h));
            }));

            table.Set("box", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double x = RequireNumber(args, offset, "screen.box(x, y, w, h [, text])");
                double y = RequireNumber(args, offset + 1, "screen.box(x, y, w, h [, text])");
                double w = RequireNumber(args, offset + 2, "screen.box(x, y, w, h [, text])");
                double h = RequireNumber(args, offset + 3, "screen.box(x, y, w, h [, text])");
                string text = args.Count > offset + 4 ? args[offset + 4].ToPrintString() : string.Empty;
                return DynValue.NewBoolean(DrawBox(mod, x, y, w, h, text));
            }));

            table.Set("line", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double x1 = RequireNumber(args, offset, "screen.line(x1, y1, x2, y2 [, thickness])");
                double y1 = RequireNumber(args, offset + 1, "screen.line(x1, y1, x2, y2 [, thickness])");
                double x2 = RequireNumber(args, offset + 2, "screen.line(x1, y1, x2, y2 [, thickness])");
                double y2 = RequireNumber(args, offset + 3, "screen.line(x1, y1, x2, y2 [, thickness])");
                double thickness = args.Count > offset + 4 && !args[offset + 4].IsNil() ? RequireNumber(args, offset + 4, "screen thickness") : 1.0;
                return DynValue.NewBoolean(DrawLine(mod, x1, y1, x2, y2, thickness));
            }));

            table.Set("circle", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double x = RequireNumber(args, offset, "screen.circle(x, y, radius [, segments, thickness])");
                double y = RequireNumber(args, offset + 1, "screen.circle(x, y, radius [, segments, thickness])");
                double radius = RequireNumber(args, offset + 2, "screen.circle(x, y, radius [, segments, thickness])");
                int segments = args.Count > offset + 3 && !args[offset + 3].IsNil() ? RequireInt(args, offset + 3, "screen.circle segments", 8, 128) : 32;
                double thickness = args.Count > offset + 4 && !args[offset + 4].IsNil() ? RequireNumber(args, offset + 4, "screen thickness") : 1.0;
                return DynValue.NewBoolean(DrawCircle(mod, x, y, radius, segments, thickness));
            }));

            table.Set("triangle", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double x1 = RequireNumber(args, offset, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double y1 = RequireNumber(args, offset + 1, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double x2 = RequireNumber(args, offset + 2, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double y2 = RequireNumber(args, offset + 3, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double x3 = RequireNumber(args, offset + 4, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double y3 = RequireNumber(args, offset + 5, "screen.triangle(x1,y1,x2,y2,x3,y3 [, thickness])");
                double thickness = args.Count > offset + 6 && !args[offset + 6].IsNil() ? RequireNumber(args, offset + 6, "screen thickness") : 1.0;
                bool ok = DrawLine(mod, x1, y1, x2, y2, thickness);
                ok &= DrawLine(mod, x2, y2, x3, y3, thickness);
                ok &= DrawLine(mod, x3, y3, x1, y1, thickness);
                return DynValue.NewBoolean(ok);
            }));

            table.Set("poly", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                if (args.Count <= offset || args[offset].Type != DataType.Table)
                    throw new ScriptRuntimeException("screen.poly(points [, thickness]) expects {{x=,y=}, ...}.");
                double thickness = args.Count > offset + 1 && !args[offset + 1].IsNil() ? RequireNumber(args, offset + 1, "screen.poly thickness") : 1.0;
                return DynValue.NewBoolean(DrawPoly(mod, args[offset].Table, thickness));
            }));

            table.Set("print", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                if (args.Count <= offset) throw new ScriptRuntimeException("screen.print(text, x, y [, width, height]) requires text.");
                string text = args[offset].Type == DataType.String ? args[offset].String : args[offset].ToPrintString();
                double x = RequireNumber(args, offset + 1, "screen.print(text, x, y [, width, height])");
                double y = RequireNumber(args, offset + 2, "screen.print(text, x, y [, width, height])");
                double w = args.Count > offset + 3 && !args[offset + 3].IsNil()
                    ? RequireNumber(args, offset + 3, "screen.print width")
                    : Math.Max(32.0, WindowApi.GetWidth() - x);
                double h = args.Count > offset + 4 && !args[offset + 4].IsNil() ? RequireNumber(args, offset + 4, "screen.print height") : 28.0;
                return DynValue.NewBoolean(DrawText(mod, text, x, y, w, h));
            }));

            table.Set("getWidth", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(WindowApi.GetWidth())));
            table.Set("getHeight", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(WindowApi.GetHeight())));
            table.Set("isAvailable", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(IsAvailable)));
            return table;
        }

        public static Table MakeColor(Script script, double r, double g, double b, double a)
        {
            NormalizeColor(ref r, ref g, ref b, ref a);
            var table = new Table(script);
            table.Set("r", DynValue.NewNumber(r));
            table.Set("g", DynValue.NewNumber(g));
            table.Set("b", DynValue.NewNumber(b));
            table.Set("a", DynValue.NewNumber(a));
            return table;
        }

        public static bool ShouldDispatchDraw
        {
            get
            {
                try
                {
                    Ensure();
                    if (_eventCurrent == null) return true;
                    object? current = _eventCurrent.GetValue(null);
                    if (current == null || _eventKind == null) return true;
                    object? kind = _eventKind.GetValue(current);
                    return kind == null || string.Equals(kind.ToString(), "Repaint", StringComparison.OrdinalIgnoreCase);
                }
                catch { return true; }
            }
        }

        public static bool IsAvailable
        {
            get
            {
                Ensure();
                return _rectCtor != null && _guiColor != null && _drawTexture != null && _whiteTexture != null && _label != null;
            }
        }

        private static bool DrawRectangle(LuaModInstance mod, double x, double y, double w, double h)
        {
            if (!IsAvailable || w <= 0.0 || h <= 0.0) return false;
            return WithColor(mod, () =>
            {
                object? rect = MakeRect(x, y, w, h);
                object? texture = _whiteTexture?.GetValue(null);
                if (rect == null || texture == null || _drawTexture == null) return false;
                _drawTexture.Invoke(null, new[] { rect, texture });
                return true;
            });
        }

        private static bool DrawLine(LuaModInstance mod, double x1, double y1, double x2, double y2, double thickness)
        {
            if (!IsAvailable || _guiMatrix == null || _rotateAroundPivot == null || _vector2Ctor == null) return false;
            double dx = x2 - x1;
            double dy = y2 - y1;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.0001) return false;
            thickness = Math.Max(0.5, thickness);

            return WithColor(mod, () =>
            {
                object? oldMatrix = _guiMatrix.GetValue(null);
                try
                {
                    object pivot = _vector2Ctor.Invoke(new object[]
                    {
                        Convert.ToSingle(x1, CultureInfo.InvariantCulture),
                        Convert.ToSingle(y1, CultureInfo.InvariantCulture)
                    });
                    float angle = Convert.ToSingle(Math.Atan2(dy, dx) * (180.0 / Math.PI), CultureInfo.InvariantCulture);
                    _rotateAroundPivot.Invoke(null, new object[] { angle, pivot });
                    object? rect = MakeRect(x1, y1 - thickness * 0.5, length, thickness);
                    object? texture = _whiteTexture?.GetValue(null);
                    if (rect == null || texture == null || _drawTexture == null) return false;
                    _drawTexture.Invoke(null, new[] { rect, texture });
                    return true;
                }
                finally
                {
                    if (oldMatrix != null) _guiMatrix.SetValue(null, oldMatrix);
                }
            });
        }

        private static bool DrawCircle(LuaModInstance mod, double cx, double cy, double radius, int segments, double thickness)
        {
            if (radius <= 0.0) return false;
            bool ok = true;
            double previousX = cx + radius;
            double previousY = cy;
            for (int i = 1; i <= segments; i++)
            {
                double angle = (Math.PI * 2.0 * i) / segments;
                double x = cx + Math.Cos(angle) * radius;
                double y = cy + Math.Sin(angle) * radius;
                ok &= DrawLine(mod, previousX, previousY, x, y, thickness);
                previousX = x;
                previousY = y;
            }
            return ok;
        }

        private static bool DrawPoly(LuaModInstance mod, Table points, double thickness)
        {
            var parsed = new System.Collections.Generic.List<System.Tuple<double, double>>();
            for (int i = 1; i <= 128; i++)
            {
                DynValue point = points.Get(i);
                if (point.IsNil()) break;
                if (point.Type != DataType.Table) continue;
                DynValue x = point.Table.Get("x");
                DynValue y = point.Table.Get("y");
                if (x.Type == DataType.Number && y.Type == DataType.Number)
                {
                    if (double.IsNaN(x.Number) || double.IsInfinity(x.Number) || double.IsNaN(y.Number) || double.IsInfinity(y.Number))
                        throw new ScriptRuntimeException("screen.poly point coordinates must be finite numbers.");
                    parsed.Add(System.Tuple.Create(x.Number, y.Number));
                }
            }
            if (parsed.Count < 2) return false;
            bool ok = true;
            for (int i = 1; i < parsed.Count; i++)
                ok &= DrawLine(mod, parsed[i - 1].Item1, parsed[i - 1].Item2, parsed[i].Item1, parsed[i].Item2, thickness);
            if (parsed.Count > 2)
                ok &= DrawLine(mod, parsed[parsed.Count - 1].Item1, parsed[parsed.Count - 1].Item2, parsed[0].Item1, parsed[0].Item2, thickness);
            return ok;
        }

        private static bool DrawBox(LuaModInstance mod, double x, double y, double w, double h, string text)
        {
            if (_box == null) return DrawRectangle(mod, x, y, w, h);
            return WithColor(mod, () =>
            {
                object? rect = MakeRect(x, y, w, h);
                if (rect == null) return false;
                _box.Invoke(null, new object?[] { rect, text ?? string.Empty });
                return true;
            });
        }

        private static bool DrawText(LuaModInstance mod, string text, double x, double y, double w, double h)
        {
            if (!IsAvailable || _label == null) return false;
            return WithColor(mod, () =>
            {
                object? rect = MakeRect(x, y, w, h);
                if (rect == null) return false;
                _label.Invoke(null, new object?[] { rect, text ?? string.Empty });
                return true;
            });
        }

        private static bool WithColor(LuaModInstance mod, Func<bool> draw)
        {
            try
            {
                Ensure();
                if (_guiColor == null || _colorCtor == null) return false;
                object? old = _guiColor.GetValue(null);
                object color = _colorCtor.Invoke(new object[]
                {
                    Convert.ToSingle(mod.DrawR, CultureInfo.InvariantCulture),
                    Convert.ToSingle(mod.DrawG, CultureInfo.InvariantCulture),
                    Convert.ToSingle(mod.DrawB, CultureInfo.InvariantCulture),
                    Convert.ToSingle(mod.DrawA, CultureInfo.InvariantCulture)
                });
                _guiColor.SetValue(null, color);
                try { return draw(); }
                finally { if (old != null) _guiColor.SetValue(null, old); }
            }
            catch { return false; }
        }

        private static object? MakeRect(double x, double y, double w, double h)
        {
            if (_rectCtor == null) return null;
            return _rectCtor.Invoke(new object[]
            {
                Convert.ToSingle(x, CultureInfo.InvariantCulture),
                Convert.ToSingle(y, CultureInfo.InvariantCulture),
                Convert.ToSingle(w, CultureInfo.InvariantCulture),
                Convert.ToSingle(h, CultureInfo.InvariantCulture)
            });
        }

        private static void ReadColor(CallbackArguments args, int offset, out double r, out double g, out double b, out double a)
        {
            if (args.Count > offset && args[offset].Type == DataType.Table)
            {
                Table t = args[offset].Table;
                r = TableNumber(t, "r", 1.0);
                g = TableNumber(t, "g", 1.0);
                b = TableNumber(t, "b", 1.0);
                a = TableNumber(t, "a", 1.0);
            }
            else
            {
                r = RequireNumber(args, offset, "screen.setColor(r, g, b [, a])");
                g = RequireNumber(args, offset + 1, "screen.setColor(r, g, b [, a])");
                b = RequireNumber(args, offset + 2, "screen.setColor(r, g, b [, a])");
                a = args.Count > offset + 3 && !args[offset + 3].IsNil() ? RequireNumber(args, offset + 3, "screen.setColor alpha") : 1.0;
            }
            NormalizeColor(ref r, ref g, ref b, ref a);
        }

        private static void NormalizeColor(ref double r, ref double g, ref double b, ref double a)
        {
            r = NormalizeComponent(r);
            g = NormalizeComponent(g);
            b = NormalizeComponent(b);
            a = NormalizeComponent(a);
        }

        private static double NormalizeComponent(double value)
        {
            if (value > 1.0) value /= 255.0;
            return Clamp01(value);
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
        private static double TableNumber(Table table, string key, double fallback)
        {
            DynValue value = table.Get(key);
            if (value.IsNil()) return fallback;
            if (value.Type != DataType.Number || double.IsNaN(value.Number) || double.IsInfinity(value.Number))
                throw new ScriptRuntimeException("screen color component '" + key + "' must be a finite number.");
            return value.Number;
        }

        private static double RequireNumber(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Number)
                throw new ScriptRuntimeException(usage + " expects numbers.");
            double value = args[index].Number;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ScriptRuntimeException(usage + " expects finite numbers.");
            return value;
        }

        private static int RequireInt(CallbackArguments args, int index, string usage, int min, int max)
        {
            double raw = RequireNumber(args, index, usage);
            if (Math.Abs(raw - Math.Round(raw)) > 0.0000001 || raw < min || raw > max)
                throw new ScriptRuntimeException(usage + $" expects an integer from {min} to {max}.");
            return (int)raw;
        }

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            return args.Count > 0 && args[0].Type == DataType.Table && ReferenceEquals(args[0].Table, table) ? 1 : 0;
        }

        private static void Ensure()
        {
            if (_guiType != null) return;

            _guiType = ReflectionBridge.FindTypeExact("UnityEngine.GUI");
            _rectType = ReflectionBridge.FindTypeExact("UnityEngine.Rect");
            _colorType = ReflectionBridge.FindTypeExact("UnityEngine.Color");
            _texture2DType = ReflectionBridge.FindTypeExact("UnityEngine.Texture2D");
            _eventType = ReflectionBridge.FindTypeExact("UnityEngine.Event");
            _guiUtilityType = ReflectionBridge.FindTypeExact("UnityEngine.GUIUtility");
            _vector2Type = ReflectionBridge.FindTypeExact("UnityEngine.Vector2");

            _rectCtor = _rectType?.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            _colorCtor = _colorType?.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            _vector2Ctor = _vector2Type?.GetConstructor(new[] { typeof(float), typeof(float) });
            _guiColor = _guiType?.GetProperty("color", BindingFlags.Public | BindingFlags.Static);
            _whiteTexture = _texture2DType?.GetProperty("whiteTexture", BindingFlags.Public | BindingFlags.Static);
            _eventCurrent = _eventType?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
            _eventKind = _eventType?.GetProperty("type", BindingFlags.Public | BindingFlags.Instance);
            _guiMatrix = _guiType?.GetProperty("matrix", BindingFlags.Public | BindingFlags.Static);
            if (_guiUtilityType != null && _vector2Type != null)
                _rotateAroundPivot = _guiUtilityType.GetMethod("RotateAroundPivot", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(float), _vector2Type }, null);

            if (_guiType != null && _rectType != null)
            {
                MethodInfo[] methods = _guiType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                _drawTexture = methods.FirstOrDefault(m =>
                {
                    if (m.Name != "DrawTexture") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == _rectType;
                });
                _label = methods.FirstOrDefault(m =>
                {
                    if (m.Name != "Label") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == _rectType && p[1].ParameterType == typeof(string);
                });
                _box = methods.FirstOrDefault(m =>
                {
                    if (m.Name != "Box") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == _rectType && p[1].ParameterType == typeof(string);
                });
            }
        }
    }
}
