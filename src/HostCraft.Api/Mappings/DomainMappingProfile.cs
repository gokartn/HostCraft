using AutoMapper;
using HostCraft.Api.Models.Domains;
using HostCraft.Core.Entities;

namespace HostCraft.Api.Mappings;

/// <summary>
/// AutoMapper profile for Domain entity mappings
/// </summary>
public class DomainMappingProfile : Profile
{
    public DomainMappingProfile()
    {
        // Domain to DomainDto
        CreateMap<Domain, DomainDto>()
            .ForMember(dest => dest.Url, opt => opt.MapFrom(src => src.GetUrl()));

        // CreateDomainRequest to Domain
        CreateMap<CreateDomainRequest, Domain>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => Guid.NewGuid()))
            .ForMember(dest => dest.ApplicationId, opt => opt.Ignore())
            .ForMember(dest => dest.Host, opt => opt.MapFrom(src => src.Host.ToLowerInvariant().Trim()))
            .ForMember(dest => dest.Path, opt => opt.MapFrom(src => src.Path ?? "/"))
            .ForMember(dest => dest.HttpsEnabled, opt => opt.MapFrom(src => src.HttpsEnabled ?? true))
            .ForMember(dest => dest.ForceHttps, opt => opt.MapFrom(src => src.ForceHttps ?? true))
            .ForMember(dest => dest.WebSocketEnabled, opt => opt.MapFrom(src => src.WebSocketEnabled ?? true))
            .ForMember(dest => dest.CompressionEnabled, opt => opt.MapFrom(src => src.CompressionEnabled ?? true))
            .ForMember(dest => dest.BasicAuthEnabled, opt => opt.MapFrom(src => src.BasicAuthEnabled ?? false))
            .ForMember(dest => dest.RateLimitRps, opt => opt.MapFrom(src => src.RateLimitRps ?? 0))
            .ForMember(dest => dest.MaxBodySizeMb, opt => opt.MapFrom(src => src.MaxBodySizeMb ?? 0))
            .ForMember(dest => dest.StripPathPrefix, opt => opt.MapFrom(src => src.StripPathPrefix ?? false))
            .ForMember(dest => dest.PathBasedRouting, opt => opt.MapFrom(src => src.PathBasedRouting ?? false))
            .ForMember(dest => dest.ProxyProtocol, opt => opt.MapFrom(src => src.Protocol ?? Core.Enums.ProxyProtocol.Http))
            .ForMember(dest => dest.DnsStatus, opt => opt.MapFrom(src => "pending"))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
            .ForMember(dest => dest.Application, opt => opt.Ignore())
            .ForMember(dest => dest.Certificate, opt => opt.Ignore())
            .ForMember(dest => dest.CertificateId, opt => opt.Ignore())
            .ForMember(dest => dest.LastDnsCheck, opt => opt.Ignore())
            .ForMember(dest => dest.DnsError, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore());
    }
}
