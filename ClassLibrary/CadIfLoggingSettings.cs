/////////////////////////////////////////////////////////////////////////////////////
//  File:   CadIfLoggingSettings.cs                                 9 Jun 23 PHR
/////////////////////////////////////////////////////////////////////////////////////

namespace Ng911CadIfLib;

/// <summary>
/// Class for specifying the settings for NG9-1-1 event logging for the Ng911CadIfServer class.
/// </summary>
public class CadIfLoggingSettings
{
    /// <summary>
    /// Element identifier (Section 2.1.3 of NENA-STA-010.3) of the element that logged the event.
    /// Required.
    /// </summary>
    public string ElementId { get; set; }

    /// <summary>
    /// AgencyId (Section 2.1.1 of NENA-STA-010.3) of the agency that logged the event. Required.
    /// </summary>
    public string AgencyId { get; set; }

    /// <summary>
    /// Agent identifier of the agent within the Agency. Optional
    /// </summary>
    public string AgencyAgentId { get; set; }

    /// <summary>
    /// Identifier of the operator position that is handling the call. Optional
    /// </summary>
    public string AgencyPositionId { get; set; }
}
