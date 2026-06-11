namespace LuaDC1.Format;

/// <summary>
/// A local-variable debug entry. BF1 scripts are usually stripped (no locals recorded),
/// in which case the decompiler synthesizes names; when present, <see cref="StartPc"/> and
/// <see cref="EndPc"/> scope the variable.
/// </summary>
public readonly record struct LocVar(string Name, int StartPc, int EndPc);
