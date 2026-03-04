using Opc.Ua;
using Opc.Ua.Client;

namespace Prediktor.PythonService.Services;

/// <summary>
/// Provides OPC UA data access methods that are exposed to the Python runtime.
/// </summary>
public class OpcUaDataService
{
    private readonly OpcUaConnectionService _connection;
    private readonly ILogger<OpcUaDataService> _logger;

    public OpcUaDataService(OpcUaConnectionService connection, ILogger<OpcUaDataService> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    /// <summary>
    /// Reads current values for one or more OPC UA tags.
    /// Returns a list of [Value, QualityCode, Timestamp] arrays.
    /// </summary>
    /// <param name="tagNames">One or more NodeId strings (e.g. "ns=2;s=MyTag").</param>
    public List<object?[]> GetValues(params string[] tagNames)
    {
        var results = new List<object?[]>();

        if (!_connection.IsConnected || _connection.Session == null)
        {
            _logger.LogWarning("GetValues: OPC UA session is not connected.");
            return results;
        }

        var nodesToRead = new ReadValueIdCollection(tagNames.Select(t => new ReadValueId
        {
            NodeId = NodeId.Parse(t),
            AttributeId = Attributes.Value
        }));

        // Synchronous read is deprecated in favour of ReadAsync; however these data-access
        // methods are called synchronously from the Python runtime, so the sync overload is used.
#pragma warning disable CS0618
        _connection.Session.Read(null, 0, TimestampsToReturn.Both, nodesToRead,
            out DataValueCollection dataValues, out _);
#pragma warning restore CS0618

        foreach (var dv in dataValues)
        {
            results.Add(new object?[]
            {
                dv.Value,
                dv.StatusCode.Code,
                dv.SourceTimestamp.ToString("O")
            });
        }

        return results;
    }

    /// <summary>
    /// Reads the time-average of an OPC UA tag over the specified time range.
    /// Returns a list of [Value, QualityCode, Timestamp] arrays.
    /// </summary>
    /// <param name="tagName">NodeId string of the tag.</param>
    /// <param name="start">Start time in ISO 8601 format (e.g. "2024-01-01T00:00:00Z").</param>
    /// <param name="end">End time in ISO 8601 format.</param>
    /// <param name="intervalMs">Processing interval in milliseconds (e.g. 3600000 for 1 hour).</param>
    public List<object?[]> GetAvg(string tagName, string start, string end, double intervalMs)
    {
        if (intervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Processing interval must be greater than zero.");

        var startDt = ParseTimestamp(start, nameof(start));
        var endDt = ParseTimestamp(end, nameof(end));

        if (startDt >= endDt)
            throw new ArgumentException("'start' must be earlier than 'end'.", nameof(start));

        return GetAggregated(tagName, ObjectIds.AggregateFunction_TimeAverage, startDt, endDt, intervalMs);
    }

    /// <summary>
    /// Reads an OPC UA aggregated value for a tag over the specified time range.
    /// Returns a list of [Value, QualityCode, Timestamp] arrays.
    /// </summary>
    /// <param name="tagName">NodeId string of the tag.</param>
    /// <param name="aggregation">
    /// Aggregation function name, e.g. "Average", "Minimum", "Maximum",
    /// "Count", "Total", "TimeAverage", "Interpolative".
    /// </param>
    /// <param name="start">Start time in ISO 8601 format (e.g. "2024-01-01T00:00:00Z").</param>
    /// <param name="end">End time in ISO 8601 format.</param>
    /// <param name="intervalMs">Processing interval in milliseconds (e.g. 3600000 for 1 hour).</param>
    public List<object?[]> GetAgg(string tagName, string aggregation, string start, string end, double intervalMs)
    {
        if (intervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(intervalMs), "Processing interval must be greater than zero.");

        var startDt = ParseTimestamp(start, nameof(start));
        var endDt = ParseTimestamp(end, nameof(end));

        if (startDt >= endDt)
            throw new ArgumentException("'start' must be earlier than 'end'.", nameof(start));

        var aggregateNodeId = ResolveAggregateFunction(aggregation);
        return GetAggregated(tagName, aggregateNodeId, startDt, endDt, intervalMs);
    }

    /// <summary>
    /// Writes a value to an OPC UA tag.
    /// </summary>
    /// <param name="tagName">NodeId string of the tag.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="quality">OPC UA StatusCode (0 = Good).</param>
    /// <param name="timestamp">Source timestamp in ISO 8601 format.</param>
    public void SetValue(string tagName, object value, uint quality, string timestamp)
    {
        if (!_connection.IsConnected || _connection.Session == null)
        {
            _logger.LogWarning("SetValue: OPC UA session is not connected.");
            return;
        }

        if (!DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
        {
            _logger.LogWarning("SetValue: Could not parse timestamp '{Timestamp}' as ISO 8601. Using UtcNow.", timestamp);
            ts = DateTime.UtcNow;
        }

        var nodesToWrite = new WriteValueCollection
        {
            new WriteValue
            {
                NodeId = NodeId.Parse(tagName),
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value), new StatusCode(quality), ts, ts)
            }
        };

        // Synchronous write is deprecated in favour of WriteAsync; used here because
        // SetValue is called synchronously from the Python runtime.
#pragma warning disable CS0618
        _connection.Session.Write(null, nodesToWrite, out StatusCodeCollection writeResults, out _);
#pragma warning restore CS0618

        if (writeResults.Count > 0 && StatusCode.IsBad(writeResults[0]))
        {
            _logger.LogWarning("SetValue for {TagName} returned bad status: {Status}", tagName, writeResults[0]);
        }
    }

    private List<object?[]> GetAggregated(
        string tagName,
        NodeId aggregateFunctionId,
        DateTime start,
        DateTime end,
        double processingIntervalMs)
    {
        var results = new List<object?[]>();

        if (!_connection.IsConnected || _connection.Session == null)
        {
            _logger.LogWarning("GetAgg: OPC UA session is not connected.");
            return results;
        }

        var details = new ReadProcessedDetails
        {
            StartTime = start,
            EndTime = end,
            ProcessingInterval = processingIntervalMs,
            AggregateType = new NodeIdCollection { aggregateFunctionId },
            AggregateConfiguration = new AggregateConfiguration { UseServerCapabilitiesDefaults = true }
        };

        var nodesToRead = new HistoryReadValueIdCollection
        {
            new HistoryReadValueId { NodeId = NodeId.Parse(tagName) }
        };

        // Synchronous HistoryRead is deprecated in favour of HistoryReadAsync; used here because
        // GetAvg/GetAgg are called synchronously from the Python runtime.
#pragma warning disable CS0618
        _connection.Session.HistoryRead(
            null,
            new ExtensionObject(details),
            TimestampsToReturn.Both,
            false,
            nodesToRead,
            out HistoryReadResultCollection historyResults,
            out _);
#pragma warning restore CS0618

        if (historyResults?.Count > 0
            && historyResults[0].HistoryData?.Body is HistoryData historyData)
        {
            foreach (var dv in historyData.DataValues)
            {
                results.Add(new object?[]
                {
                    dv.Value,
                    dv.StatusCode.Code,
                    dv.SourceTimestamp.ToString("O")
                });
            }
        }

        return results;
    }

    private static NodeId ResolveAggregateFunction(string aggregation)
    {
        return aggregation.ToLowerInvariant() switch
        {
            "average" or "avg" => ObjectIds.AggregateFunction_Average,
            "timeaverage" or "timeavg" => ObjectIds.AggregateFunction_TimeAverage,
            "minimum" or "min" => ObjectIds.AggregateFunction_Minimum,
            "maximum" or "max" => ObjectIds.AggregateFunction_Maximum,
            "count" => ObjectIds.AggregateFunction_Count,
            "total" => ObjectIds.AggregateFunction_Total,
            "interpolative" => ObjectIds.AggregateFunction_Interpolative,
            _ => throw new ArgumentException($"Unknown OPC UA aggregation function: '{aggregation}'.", nameof(aggregation))
        };
    }

    private static DateTime ParseTimestamp(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Timestamp must not be null or empty. Expected an ISO 8601 string.", paramName);

        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        throw new ArgumentException($"Could not parse '{value}' as an ISO 8601 timestamp.", paramName);
    }
}
