using System;

namespace Panopticon.Geospatial.Payload.Serialization;

/// <summary>The JSON document violates the payload schema; the payload input is recoverably invalid.</summary>
internal sealed class PayloadJsonSchemaException(string message) : Exception(message);

/// <summary>A payload byte or structural limit was exceeded before the offending content was materialized.</summary>
internal sealed class PayloadJsonLimitException(string message) : Exception(message);
