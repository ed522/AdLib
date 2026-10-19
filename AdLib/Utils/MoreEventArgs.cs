using System;
using System.Diagnostics.CodeAnalysis;

namespace AdLib.Utils;

// these are all standard templates, don't bother with unused type warnings (they'll be useful eventually)
// ReSharper disable file UnusedType.Global
// ReSharper disable file UnusedMember.Global
// ReSharper disable file UnusedAutoPropertyAccessor.Global
public class EventArgs<T>() : EventArgs
{
    public required T Value { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T value) : this() => this.Value = value;
}

public class EventArgs<T1, T2>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
    }
}

public class EventArgs<T1, T2, T3>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
    }
}

public class EventArgs<T1, T2, T3, T4>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }
    public required T4 Value4 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3, T4 value4) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
        this.Value4 = value4;
    }
}

public class EventArgs<T1, T2, T3, T4, T5>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }
    public required T4 Value4 { get; set; }
    public required T5 Value5 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
        this.Value4 = value4;
        this.Value5 = value5;
    }
}

public class EventArgs<T1, T2, T3, T4, T5, T6>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }
    public required T4 Value4 { get; set; }
    public required T5 Value5 { get; set; }
    public required T6 Value6 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
        this.Value4 = value4;
        this.Value5 = value5;
        this.Value6 = value6;
    }
}

public class EventArgs<T1, T2, T3, T4, T5, T6, T7>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }
    public required T4 Value4 { get; set; }
    public required T5 Value5 { get; set; }
    public required T6 Value6 { get; set; }
    public required T7 Value7 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
        this.Value4 = value4;
        this.Value5 = value5;
        this.Value6 = value6;
        this.Value7 = value7;
    }
}

public class EventArgs<T1, T2, T3, T4, T5, T6, T7, T8>() : EventArgs
{
    public required T1 Value1 { get; set; }
    public required T2 Value2 { get; set; }
    public required T3 Value3 { get; set; }
    public required T4 Value4 { get; set; }
    public required T5 Value5 { get; set; }
    public required T6 Value6 { get; set; }
    public required T7 Value7 { get; set; }
    public required T8 Value8 { get; set; }

    [SetsRequiredMembers]
    public EventArgs(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8) : this()
    {
        this.Value1 = value1;
        this.Value2 = value2;
        this.Value3 = value3;
        this.Value4 = value4;
        this.Value5 = value5;
        this.Value6 = value6;
        this.Value7 = value7;
        this.Value8 = value8;
    }
}