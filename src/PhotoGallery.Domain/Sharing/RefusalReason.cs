namespace PhotoGallery.Domain.Sharing;

/// <summary>Why a machine's answers were not taken.</summary>
public enum RefusalReason
{
    /// <summary>
    /// It is running a newer release and its decisions are in a shape this one
    /// does not know. Refused whole rather than partly applied: half a schema
    /// applied is a library that looks fine and disagrees with itself.
    /// </summary>
    SchemaTooNew = 0,

    /// <summary>
    /// Its clock is far enough ahead that every answer it makes would override
    /// everybody else's for as long as the error lasts, including answers made
    /// long afterwards. Nothing about the result would look broken; it would
    /// simply always agree with one machine.
    /// </summary>
    ClockTooFarAhead = 1,

    /// <summary>
    /// It shares no source with this library, so there is nothing to say. Named
    /// rather than reported as an exchange that did nothing and succeeded.
    /// </summary>
    NoSourceInCommon = 2,
}
