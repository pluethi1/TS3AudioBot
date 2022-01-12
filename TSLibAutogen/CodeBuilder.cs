using System;
using System.Text;

namespace TSLibAutogen;

public class CodeBuilder
{
	public StringBuilder Strb = new();
	public int Level { get; set; } = 0;
	public string Indent => new('\t', Level);

	public CodeBuilder(bool writeHeader = true)
	{
		if (writeHeader)
			WriteHeader();
	}

	public void PushLevel() => Level++;
	public void PopLevel()
	{
		Level--;
		if (Level < 0)
		{
			Level = 0;
			Strb.Append("/* ERROR Indentation underflow ERROR */"); // TODO diag error
		}
	}

	public void WriteHeader()
	{
		// The TSLib License
		AppendLine(Util.LicenseHeader);
		// Make clear this is autogenerated. Even if the IDE warns us already
		AppendLine("// <auto-generated />");
		// Nullable annotations need to be *explicitely* enabled for autogenerated files
		AppendLine("#nullable enable");
		AppendLine();
	}

	public void AppendRaw(string s) => Strb.Append(s);

	public void AppendLine() => AppendSingleLine("");
	public void AppendLine(string s)
	{
		foreach (var line in s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
			AppendSingleLine(line);
		AutoPushLevel(s);
	}

	private void AppendSingleLine(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
			Strb.AppendLine();
		else
			Strb.Append(Indent).AppendLine(s);
	}

	public void AppendFormatLine(string f, params object?[] o)
	{
		Strb.Append(Indent).AppendFormat(f, o).AppendLine();
		AutoPushLevel(f);
	}

	private void AutoPushLevel(string s)
	{
		if (s.AsSpan().TrimEnd().EndsWith("{".AsSpan()))
			PushLevel();
	}

	public void PopCloseBrace()
	{
		PopLevel();
		AppendLine("}");
	}

	public override string ToString() => Strb.ToString();
}
