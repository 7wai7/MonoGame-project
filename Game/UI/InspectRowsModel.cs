using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Xna.Framework;
using MonoGame_project.Ecs;
using MonoGame_project.Engine.Rendering.ThreeD;
using MonoGame_project.Engine.Terrain;

namespace MonoGame_project.Game.UI;

/// <summary>Inspector data and expansion state, independent of Gum layout and input.</summary>
internal sealed class InspectRowsModel
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);
    private static readonly Dictionary<Type, MemberInfo[]> MemberCache = new();

    private readonly List<Row> _rows = [];
    private readonly List<Type> _componentTypes = [];
    private readonly HashSet<Type> _expandedComponents = [];
    private readonly Dictionary<Type, object> _snapshotValues = [];
    private readonly List<InspectRowDisplay> _displayRows = [];
    private int _knownStructureVersion = -1;
    private long _lastSnapshotAt;
    private bool _diagnosticsExpanded;

    public int Revision { get; private set; }

    public void Reset()
    {
        _knownStructureVersion = -1;
        _lastSnapshotAt = 0;
        _diagnosticsExpanded = false;
        _componentTypes.Clear();
        _expandedComponents.Clear();
        _snapshotValues.Clear();
        _rows.Clear();
        _displayRows.Clear();
        Revision++;
    }

    public void ClearSnapshots()
    {
        _snapshotValues.Clear();
        _lastSnapshotAt = 0;
    }

    public bool SampleIfDue(World world, Entity entity)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastSnapshotAt != 0 &&
            Stopwatch.GetElapsedTime(_lastSnapshotAt, now) < SnapshotInterval)
        {
            return false;
        }

        _lastSnapshotAt = now;
        _snapshotValues.Clear();
        foreach (var snapshot in world.GetComponentSnapshots(entity))
        {
            _snapshotValues.Add(snapshot.Type, snapshot.Value);
        }

        if (_knownStructureVersion != world.StructureVersion)
        {
            _knownStructureVersion = world.StructureVersion;
            var types = _snapshotValues.Keys.OrderBy(type => type.Name).ToArray();
            if (!_componentTypes.SequenceEqual(types))
            {
                _componentTypes.Clear();
                _componentTypes.AddRange(types);
                _expandedComponents.RemoveWhere(type => !_componentTypes.Contains(type));
                RebuildRows();
            }
        }

        return true;
    }

    public void ToggleRow(int index)
    {
        if ((uint)index >= (uint)_rows.Count)
        {
            return;
        }

        var row = _rows[index];
        if (row.Kind == RowKind.DiagnosticsHeader)
        {
            _diagnosticsExpanded = !_diagnosticsExpanded;
        }
        else if (row.Kind == RowKind.ComponentHeader && row.ComponentType is { } type)
        {
            if (!_expandedComponents.Add(type))
            {
                _expandedComponents.Remove(type);
            }
        }
        else
        {
            return;
        }

        RebuildRows();
    }

    public void ExpandFirstComponent()
    {
        if (_componentTypes.Count == 0 || !_expandedComponents.Add(_componentTypes[0])) return;
        RebuildRows();
    }

    public IReadOnlyList<InspectRowDisplay> FormatRows(Entity entity, MeshRenderer renderer)
    {
        _displayRows.Clear();
        foreach (var row in _rows)
        {
            var text = FormatRow(row, entity, renderer);
            var secondary = row.Kind is RowKind.ComponentValue or RowKind.Diagnostic;
            var clickable = row.Kind is RowKind.DiagnosticsHeader or RowKind.ComponentHeader;
            _displayRows.Add(new InspectRowDisplay(text, secondary, clickable));
        }

        return _displayRows;
    }

    private void RebuildRows()
    {
        _rows.Clear();
        _rows.Add(new Row(RowKind.DiagnosticsHeader, "Technical details"));
        if (_diagnosticsExpanded)
        {
            _rows.Add(new Row(RowKind.Diagnostic, "Entity ID", Diagnostic: DiagnosticField.EntityId));
            _rows.Add(new Row(RowKind.Diagnostic, "Generation", Diagnostic: DiagnosticField.Generation));
            _rows.Add(new Row(RowKind.Diagnostic, "Vertices", Diagnostic: DiagnosticField.Vertices));
            _rows.Add(new Row(RowKind.Diagnostic, "Triangles", Diagnostic: DiagnosticField.Triangles));
        }

        _rows.Add(new Row(RowKind.SectionHeader, $"Components ({_componentTypes.Count})"));
        foreach (var type in _componentTypes)
        {
            _rows.Add(new Row(RowKind.ComponentHeader, type.Name, type));
            if (!_expandedComponents.Contains(type))
            {
                continue;
            }

            var members = GetMembers(type);
            if (members.Length == 0)
            {
                _rows.Add(new Row(RowKind.ComponentValue, "(no public data)", type));
            }
            else
            {
                foreach (var member in members)
                {
                    _rows.Add(new Row(RowKind.ComponentValue, member.Name, type, member));
                }
            }
        }

        Revision++;
    }

    private string FormatRow(Row row, Entity entity, MeshRenderer renderer)
    {
        switch (row.Kind)
        {
            case RowKind.DiagnosticsHeader:
                return $"{(_diagnosticsExpanded ? "-" : "+")}  {row.Label}";
            case RowKind.SectionHeader:
                return row.Label;
            case RowKind.ComponentHeader:
                return $"{(_expandedComponents.Contains(row.ComponentType!) ? "-" : "+")}  {row.Label}";
            case RowKind.Diagnostic:
                return row.Diagnostic switch
                {
                    DiagnosticField.EntityId => $"  Entity ID  {entity.Id}",
                    DiagnosticField.Generation => $"  Generation  {entity.Generation}",
                    DiagnosticField.Vertices => $"  Vertices  {renderer.Mesh.VertexCount:N0}",
                    DiagnosticField.Triangles => $"  Triangles  {renderer.Mesh.PrimitiveCount:N0}",
                    _ => ""
                };
            case RowKind.ComponentValue:
                if (row.Member is null)
                {
                    return "  (no public data)";
                }

                if (!_snapshotValues.TryGetValue(row.ComponentType!, out var component))
                {
                    return $"  {row.Label}  —";
                }

                try
                {
                    var value = row.Member switch
                    {
                        FieldInfo field => field.GetValue(component),
                        PropertyInfo property => property.GetValue(component),
                        _ => null
                    };
                    return $"  {row.Label}  {FormatValue(value)}";
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return $"  {row.Label}  <unavailable>";
                }
            default:
                return "";
        }
    }

    private static MemberInfo[] GetMembers(Type type)
    {
        if (MemberCache.TryGetValue(type, out var members))
        {
            return members;
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
        members = fields.Length > 0
            ? fields.OrderBy(field => field.Name).Cast<MemberInfo>().ToArray()
            : type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name)
                .Cast<MemberInfo>().ToArray();
        MemberCache.Add(type, members);
        return members;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        float number => number.ToString("0.###", CultureInfo.InvariantCulture),
        double number => number.ToString("0.###", CultureInfo.InvariantCulture),
        Vector2 v => $"({FormatValue(v.X)}, {FormatValue(v.Y)})",
        Vector3 v => $"({FormatValue(v.X)}, {FormatValue(v.Y)}, {FormatValue(v.Z)})",
        Quaternion q => $"({FormatValue(q.X)}, {FormatValue(q.Y)}, {FormatValue(q.Z)}, {FormatValue(q.W)})",
        Color c => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}",
        Mesh mesh => $"Mesh ({mesh.VertexCount}v, {mesh.PrimitiveCount}t)",
        TerrainHeightField heights => $"{heights.CellCount} x {heights.CellCount} cells",
        bool flag => flag ? "true" : "false",
        _ => value.ToString() ?? ""
    };

    private enum RowKind { DiagnosticsHeader, Diagnostic, SectionHeader, ComponentHeader, ComponentValue }
    private enum DiagnosticField { None, EntityId, Generation, Vertices, Triangles }
    private readonly record struct Row(RowKind Kind, string Label, Type? ComponentType = null,
        MemberInfo? Member = null, DiagnosticField Diagnostic = DiagnosticField.None);
}

internal readonly record struct InspectRowDisplay(string Text, bool Secondary, bool Clickable);
