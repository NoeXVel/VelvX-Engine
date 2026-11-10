using System.Windows.Input;
using VelvX.Engine.Core;
using VelvX.Engine.Language;

namespace VelvX.Engine.Runtime;

/// <summary>Estado do teclado e resolução das ações configuradas no projeto.</summary>
public sealed class InputState
{
    private readonly HashSet<Key> _keys = [];

    public InputState(InputMap? map = null)
    {
        Map = map ?? InputMap.CreateDefault();
    }

    public InputMap Map { get; set; }

    public void Set(Key key, bool down)
    {
        if (down) _keys.Add(key);
        else _keys.Remove(key);
    }

    public void Clear() => _keys.Clear();

    public bool IsDown(Key key) => _keys.Contains(key);

    public bool IsActionDown(string action)
    {
        var normalized = Normalize(action);
        if (string.IsNullOrWhiteSpace(normalized)) return false;

        var mapped = Map.Find(normalized);
        if (mapped is not null)
        {
            foreach (var keyName in mapped.Keys)
            {
                if (InputKeys.TryParse(keyName, out var key) && IsDown(key)) return true;
            }
            return false;
        }

        if (IsRight(normalized)) return IsDown(Key.D) || IsDown(Key.Right);
        if (IsLeft(normalized)) return IsDown(Key.A) || IsDown(Key.Left);
        if (IsDownAction(normalized)) return IsDown(Key.S) || IsDown(Key.Down);
        if (IsUpAction(normalized)) return IsDown(Key.W) || IsDown(Key.Up);
        return false;
    }

    public VelvVector2 GetInputVectors(IReadOnlyList<string> actions)
    {
        var right = false;
        var left = false;
        var down = false;
        var up = false;
        var recognized = false;

        for (var index = 0; index < actions.Count; index++)
        {
            var action = Normalize(actions[index]);
            if (string.IsNullOrWhiteSpace(action)) continue;

            if (Map.Find(action) is not null || IsRight(action) || IsLeft(action) ||
                IsDownAction(action) || IsUpAction(action))
                recognized = true;

            if (IsRight(action)) right |= IsActionDown(action);
            else if (IsLeft(action)) left |= IsActionDown(action);
            else if (IsDownAction(action)) down |= IsActionDown(action);
            else if (IsUpAction(action)) up |= IsActionDown(action);
        }

        // Compatibilidade com scripts que passam quaisquer quatro nomes:
        // positivo X, negativo X, positivo Y, negativo Y.
        if (!recognized)
        {
            right = IsDown(Key.D) || IsDown(Key.Right);
            left = IsDown(Key.A) || IsDown(Key.Left);
            down = IsDown(Key.S) || IsDown(Key.Down);
            up = IsDown(Key.W) || IsDown(Key.Up);
        }

        return new VelvVector2(
            (right ? 1 : 0) - (left ? 1 : 0),
            (down ? 1 : 0) - (up ? 1 : 0));
    }

    public static string Normalize(string? action) =>
        (action ?? "").Trim().ToLowerInvariant()
            .Replace('á', 'a')
            .Replace('ã', 'a')
            .Replace('â', 'a')
            .Replace('é', 'e')
            .Replace('ê', 'e')
            .Replace('í', 'i')
            .Replace('ó', 'o')
            .Replace('ô', 'o')
            .Replace('ú', 'u')
            .Replace('ç', 'c');

    private static bool IsRight(string action) =>
        action is "d" or "right" or "direita" or "move_right" or "mover_direita" ||
        action.Contains("direita") || action.Contains("right");

    private static bool IsLeft(string action) =>
        action is "a" or "left" or "esquerda" or "move_left" or "mover_esquerda" ||
        action.Contains("esquerda") || action.Contains("left");

    private static bool IsDownAction(string action) =>
        action is "s" or "down" or "baixo" or "back" or "tras" or "atras" or "para_baixo" or "mover_baixo" ||
        action.Contains("baixo") || action.Contains("down") || action.Contains("back") ||
        action.Contains("tras");

    private static bool IsUpAction(string action) =>
        action is "w" or "up" or "cima" or "forward" or "frente" or "para_cima" or "mover_cima" ||
        action.Contains("cima") || action.Contains("up") || action.Contains("forward") ||
        action.Contains("frente");
}

public static class InputKeys
{
    private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["espaco"] = Key.Space,
        ["espaço"] = Key.Space,
        ["spacebar"] = Key.Space,
        ["seta cima"] = Key.Up,
        ["seta baixo"] = Key.Down,
        ["seta esquerda"] = Key.Left,
        ["seta direita"] = Key.Right,
        ["enter"] = Key.Enter,
        ["return"] = Key.Enter,
        ["esc"] = Key.Escape,
        ["escape"] = Key.Escape,
        ["shift"] = Key.LeftShift,
        ["leftshift"] = Key.LeftShift,
        ["rightshift"] = Key.RightShift,
        ["ctrl"] = Key.LeftCtrl,
        ["control"] = Key.LeftCtrl,
        ["alt"] = Key.LeftAlt,
        ["tab"] = Key.Tab,
        ["backspace"] = Key.Back,
        ["delete"] = Key.Delete
    };

    public static bool TryParse(string? text, out Key key)
    {
        key = Key.None;
        var value = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (Aliases.TryGetValue(value, out key)) return true;
        if (Enum.TryParse(value, ignoreCase: true, out key) && key != Key.None) return true;

        var compact = InputState.Normalize(value).Replace(" ", "");
        if (compact.Length == 1 && char.IsLetter(compact[0]))
        {
            var name = char.ToUpperInvariant(compact[0]).ToString();
            return Enum.TryParse(name, ignoreCase: true, out key);
        }

        if (compact.StartsWith("f") && int.TryParse(compact[1..], out var function) &&
            function is >= 1 and <= 12)
        {
            return Enum.TryParse("F" + function, ignoreCase: true, out key);
        }

        return false;
    }

    public static string Display(Key key) => key switch
    {
        Key.Space => "Space",
        Key.LeftShift => "LeftShift",
        Key.RightShift => "RightShift",
        Key.LeftCtrl => "LeftCtrl",
        Key.RightCtrl => "RightCtrl",
        Key.LeftAlt => "LeftAlt",
        Key.RightAlt => "RightAlt",
        Key.Enter => "Enter",
        Key.Escape => "Escape",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        _ => key.ToString()
    };
}
