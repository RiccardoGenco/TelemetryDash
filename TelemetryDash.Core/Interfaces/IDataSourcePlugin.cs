using System.ComponentModel.Composition;
using TelemetryDash.Core.Models;

namespace TelemetryDash.Core.Interfaces;

[InheritedExport(typeof(IDataSourcePlugin))]
public interface IDataSourcePlugin
{
    string Name { get; }
    IObservable<TelemetryReading> GetDataStream(CancellationToken ct);
    Task ConnectAsync();
    Task DisconnectAsync();
}
