using System.Windows.Input;
using VelvX.Engine.Language;

namespace VelvX.Engine.Runtime;

public sealed class InputState
{
    private readonly HashSet<Key> _keys = [];

    public void Set(Key key, bool down)
    {
        if (down) _keys.Add(key);
        else _keys.Remove(key);
    }

    public void Clear() => _keys.Clear();

    public bool IsDown(Key key) => _keys.Contains(key);

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

            if (IsRight(action))
            {
                recognized = true;
                right |= IsDown(Key.D) || IsDown(Key.Right);
            }
            else if (IsLeft(action))
            {
                recognized = true;
                left |= IsDown(Key.A) || IsDown(Key.Left);
            }
            else if (IsDownAction(action))
            {
                recognized = true;
                down |= IsDown(Key.S) || IsDown(Key.Down);
            }
            else if (IsUpAction(action))
            {
                recognized = true;
                up |= IsDown(Key.W) || IsDown(Key.Up);
            }
        }

        // A documentação da VelvX define a ordem dos quatro argumentos como
        // positivo X, negativo X, positivo Y e negativo Y. Quando o projeto
        // ainda não tem um mapa de ações, essa ordem funciona como padrão.
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

    private static string Normalize(string action) =>
        (action ?? "").Trim().ToLowerInvariant()
            .Replace('á', 'a')
            .Replace('ã', 'a')
            .Replace('â', 'a')
            .Replace('é', 'e')
            .Replace('í', 'i')
            .Replace('ó', 'o')
            .Replace('ú', 'u');

    private static bool IsRight(string action) =>
        action is "d" or "right" or "direita" or "move_right" ||
        action.Contains("direita") || action.Contains("right");

    private static bool IsLeft(string action) =>
        action is "a" or "left" or "esquerda" or "move_left" ||
        action.Contains("esquerda") || action.Contains("left");

    private static bool IsDownAction(string action) =>
        action is "s" or "down" or "baixo" or "back" or "tras" or "atras" or "para_baixo" ||
        action.Contains("baixo") || action.Contains("down") || action.Contains("back") ||
        action.Contains("tras");

    private static bool IsUpAction(string action) =>
        action is "w" or "up" or "cima" or "forward" or "frente" or "para_cima" ||
        action.Contains("cima") || action.Contains("up") || action.Contains("forward") ||
        action.Contains("frente");
}
