using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace VelvX.Engine.Language;

/// <summary>Realce de sintaxe da VelvX Language para o editor (AvalonEdit).</summary>
public static class VelvXHighlighting
{
    private const string Definition = """
<SyntaxDefinition name="VelvX" extensions=".velv" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="#6E7589" fontStyle="italic" />
  <Color name="String" foreground="#8EE3A8" />
  <Color name="Number" foreground="#F2C879" />
  <Color name="Keyword" foreground="#C9A7FF" fontWeight="bold" />
  <Color name="BuiltIn" foreground="#7EC8FF" />
  <Color name="Function" foreground="#FFD37E" />
  <Color name="Property" foreground="#E7D9FF" />
  <RuleSet ignoreCase="false">
    <Span color="Comment" begin="##" />
    <Span color="String" begin="&quot;" end="&quot;" />
    <Rule color="Keyword">\b(global|func|var|if|else|while|return|true|false|null|new|this|and|or|not)\b</Rule>
    <Rule color="BuiltIn">\b(console|game|Scene|Scenes|Assets|Vector2|Vector3)\b</Rule>
    <Rule color="Function">\b[A-Za-z_][A-Za-z0-9_]*(?=\s*\()</Rule>
    <Rule color="Number">\b\d+(?:\.\d+)?\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";

    public static IHighlightingDefinition Load()
    {
        using var reader = XmlReader.Create(new StringReader(Definition));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
