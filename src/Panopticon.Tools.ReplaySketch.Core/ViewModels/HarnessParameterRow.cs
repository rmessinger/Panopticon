using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Panopticon.Tools.ReplaySketch.Model.HarnessLauncher;

namespace Panopticon.Tools.ReplaySketch.ViewModels;

public sealed class HarnessParameterRow : INotifyPropertyChanged
{
    private string _text;
    private string? _error;

    public HarnessParameterRow(HarnessParameterDescriptor descriptor, bool locked = false)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        IsLocked = locked;
        _text = locked && descriptor.Kind == HarnessParameterKind.Bool ? "true" : ToText(descriptor.Default);
        Validate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public HarnessParameterDescriptor Descriptor { get; }

    public bool IsLocked { get; }

    public string Text { get => _text; set { if (IsLocked || _text == value) return; _text = value; Validate(); OnChanged(null); } }

    public string? Error { get => _error; private set => _error = value; }

    public bool IsOverridden => !string.Equals(Text, ToText(Descriptor.Default), StringComparison.Ordinal);

    public void Reset()
    {
        if (IsLocked) return;
        _text = ToText(Descriptor.Default);
        Validate();
        OnChanged(null);
    }

    public bool TryGetValue(out JsonNode? value)
    {
        Validate();
        value = null;
        if (Error is not null) return false;
        if (Text.Length == 0 && Descriptor.Default is null) return true;
        value = Descriptor.Kind switch
        {
            HarnessParameterKind.Bool => JsonValue.Create(bool.Parse(Text)),
            HarnessParameterKind.Int => JsonValue.Create(int.Parse(Text, CultureInfo.InvariantCulture)),
            HarnessParameterKind.Float => JsonValue.Create(float.Parse(Text, CultureInfo.InvariantCulture)),
            HarnessParameterKind.Json => JsonNode.Parse(Text),
            _ => JsonValue.Create(Text),
        };
        return true;
    }

    private void Validate()
    {
        if (Text.Length == 0 && Descriptor.Default is null)
        {
            Error = null;
            return;
        }

        Error = Descriptor.Kind switch
        {
            HarnessParameterKind.Bool when !bool.TryParse(Text, out _) => "Enter true or false.",
            HarnessParameterKind.Int when !int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => "Enter an integer.",
            HarnessParameterKind.Float when !float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !float.IsFinite(number) => "Enter a finite number.",
            HarnessParameterKind.Enum when !Descriptor.EnumValues.Contains(Text, StringComparer.Ordinal) => "Choose a listed value.",
            HarnessParameterKind.Json when !IsJson(Text) => "Enter valid JSON.",
            _ => null,
        };
    }

    private static bool IsJson(string value)
    {
        try { _ = JsonNode.Parse(value); return true; }
        catch { return false; }
    }

    public static string ToText(JsonNode? value) => value is null ? string.Empty
        : value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    private void OnChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
