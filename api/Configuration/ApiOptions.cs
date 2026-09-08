using System.ComponentModel.DataAnnotations;

namespace Kanbada.Api;

public sealed class ApiOptions
{
    [Required, Url]
    public string PortalOrigin { get; set; } = "http://localhost:4173";
}
