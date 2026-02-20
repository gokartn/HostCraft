using System.Linq;
using AutoMapper;
using HostCraft.Api.Models.Domains;
using HostCraft.Api.Models.Shared;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DomainCertificateInfo = HostCraft.Api.Models.Domains.CertificateInfo;

namespace HostCraft.Api.Services;

public class DomainsWorkflowService : IDomainsWorkflowService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IDomainRepository _domainRepository;
    private readonly ICertificateRepository _certificateRepository;
    private readonly ICertificateService _certificateService;
    private readonly IProxyService _proxyService;
    private readonly ITraefikService _traefikService;
    private readonly IDnsValidationService _dnsValidationService;
    private readonly IMapper _mapper;
    private readonly ILogger<DomainsWorkflowService> _logger;

    public DomainsWorkflowService(
        IApplicationRepository applicationRepository,
        IDomainRepository domainRepository,
        ICertificateRepository certificateRepository,
        ICertificateService certificateService,
        IProxyService proxyService,
        ITraefikService traefikService,
        IDnsValidationService dnsValidationService,
        IMapper mapper,
        ILogger<DomainsWorkflowService> logger)
    {
        _applicationRepository = applicationRepository;
        _domainRepository = domainRepository;
        _certificateRepository = certificateRepository;
        _certificateService = certificateService;
        _proxyService = proxyService;
        _traefikService = traefikService;
        _dnsValidationService = dnsValidationService;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<ApiActionResult<PagedResult<DomainDto>>> GetDomainsAsync(int applicationId, bool paged, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await _applicationRepository.ExistsAsync(applicationId, cancellationToken))
            return ApiActionResult<PagedResult<DomainDto>>.Fail(StatusCodes.Status404NotFound, "Application not found");

        if (!paged)
        {
            var domains = await _domainRepository.GetByApplicationIdAsync(applicationId, cancellationToken);
            var mappedDomains = _mapper.Map<IEnumerable<DomainDto>>(domains);
            return ApiActionResult<PagedResult<DomainDto>>.Ok(new PagedResult<DomainDto>(mappedDomains, mappedDomains.Count(), 1, mappedDomains.Count()));
        }

        var (items, totalCount) = await _domainRepository.GetByApplicationIdPagedAsync(applicationId, page, pageSize, cancellationToken);
        var mapped = _mapper.Map<IEnumerable<DomainDto>>(items);

        return ApiActionResult<PagedResult<DomainDto>>.Ok(new PagedResult<DomainDto>(mapped, totalCount, page, pageSize));
    }

    public async Task<ApiActionResult<DomainDto>> GetDomainAsync(int applicationId, int id, CancellationToken cancellationToken)
    {
        var domain = await _domainRepository.GetByIdAsync(id, cancellationToken);

        if (domain == null || domain.ApplicationId != applicationId)
            return ApiActionResult<DomainDto>.Fail(StatusCodes.Status404NotFound, "Domain not found");

        return ApiActionResult<DomainDto>.Ok(_mapper.Map<DomainDto>(domain));
    }

    public async Task<ApiActionResult<DomainDto>> CreateDomainAsync(int applicationId, CreateDomainRequest request, CancellationToken cancellationToken)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDomainsAsync(applicationId, cancellationToken);
        if (app == null)
            return ApiActionResult<DomainDto>.Fail(StatusCodes.Status404NotFound, "Application not found");

        if (string.IsNullOrWhiteSpace(request.Host))
            return ApiActionResult<DomainDto>.Fail(StatusCodes.Status400BadRequest, "Domain host is required");

        var path = request.Path ?? "/";
        if (await _domainRepository.ExistsByHostAndPathAsync(request.Host, path, cancellationToken))
            return ApiActionResult<DomainDto>.Fail(StatusCodes.Status400BadRequest, $"Domain {request.Host}{path} is already in use");

        var isPrimary = request.IsPrimary || !app.Domains.Any();
        if (isPrimary)
        {
            foreach (var d in app.Domains.Where(d => d.IsPrimary))
            {
                d.IsPrimary = false;
                await _domainRepository.UpdateAsync(d, cancellationToken);
            }
        }

        var desiredPort = request.Port > 0 ? request.Port : (app.Port ?? app.PublishedPort ?? 80);
        var protocol = request.Protocol ?? DomainConfigurationHelper.DetermineDefaultProtocol(app, desiredPort);
        var httpsEnabled = request.HttpsEnabled ?? DomainConfigurationHelper.DetermineDefaultHttps(protocol);
        var forceHttps = request.ForceHttps ?? DomainConfigurationHelper.DetermineDefaultForceHttps(protocol, httpsEnabled);
        var webSocketEnabled = request.WebSocketEnabled ?? protocol == ProxyProtocol.Http;
        var compressionEnabled = request.CompressionEnabled ?? protocol == ProxyProtocol.Http;

        var domain = _mapper.Map<Domain>(request);
        domain.ApplicationId = applicationId;
        domain.Port = desiredPort;
        domain.IsPrimary = isPrimary;
        domain.HttpsEnabled = httpsEnabled;
        domain.ForceHttps = forceHttps;
        domain.WebSocketEnabled = webSocketEnabled;
        domain.CompressionEnabled = compressionEnabled;
        domain.ProxyProtocol = protocol;

        // For TCP domains: TargetPort is the container's internal listening port.
        // If the user didn't specify it, default to the application's actual container port.
        // This keeps Port (external entrypoint) separate from TargetPort (container port).
        if (protocol == ProxyProtocol.Tcp && domain.TargetPort == null)
            domain.TargetPort = request.TargetPort ?? app.Port ?? app.PublishedPort ?? desiredPort;

        DomainConfigurationHelper.NormalizeDomainForProtocol(domain);

        await _domainRepository.AddAsync(domain, cancellationToken);
        _logger.LogInformation("Added domain {Host} to application {AppId}", domain.Host, applicationId);

        await _traefikService.UpdateServiceLabelsAsync(applicationId, cancellationToken);

        if (domain.ProxyProtocol == ProxyProtocol.Tcp)
            await _proxyService.EnsureTcpEntrypointAsync(domain.Port, cancellationToken);

        return ApiActionResult<DomainDto>.Ok(_mapper.Map<DomainDto>(domain), StatusCodes.Status201Created);
    }

    public async Task<ApiActionResult<DomainDto>> UpdateDomainAsync(int applicationId, int id, UpdateDomainRequest request, CancellationToken cancellationToken)
    {
        var domain = await _domainRepository.GetByIdWithApplicationAsync(id, cancellationToken);

        if (domain == null || domain.ApplicationId != applicationId)
            return ApiActionResult<DomainDto>.Fail(StatusCodes.Status404NotFound, "Domain not found");

        if (request.Host != null)
            domain.Host = request.Host.ToLowerInvariant().Trim();
        if (request.Path != null)
            domain.Path = request.Path;
        if (request.Port.HasValue)
            domain.Port = request.Port.Value;
        if (request.HttpsEnabled.HasValue)
            domain.HttpsEnabled = request.HttpsEnabled.Value;
        if (request.ForceHttps.HasValue)
            domain.ForceHttps = request.ForceHttps.Value;
        if (request.CertificateType != null)
            domain.CertificateType = request.CertificateType;
        if (request.WebSocketEnabled.HasValue)
            domain.WebSocketEnabled = request.WebSocketEnabled.Value;
        if (request.CompressionEnabled.HasValue)
            domain.CompressionEnabled = request.CompressionEnabled.Value;
        if (request.BasicAuthEnabled.HasValue)
            domain.BasicAuthEnabled = request.BasicAuthEnabled.Value;
        if (request.BasicAuthUsers != null)
            domain.BasicAuthUsers = request.BasicAuthUsers;
        if (request.RateLimitRps.HasValue)
            domain.RateLimitRps = request.RateLimitRps.Value;
        if (request.IpWhitelist != null)
            domain.IpWhitelist = request.IpWhitelist;
        if (request.MaxBodySizeMb.HasValue)
            domain.MaxBodySizeMb = request.MaxBodySizeMb.Value;
        if (request.StripPathPrefix.HasValue)
            domain.StripPathPrefix = request.StripPathPrefix.Value;
        if (request.PathBasedRouting.HasValue)
            domain.PathBasedRouting = request.PathBasedRouting.Value;
        if (request.CustomHeaders != null)
            domain.CustomHeaders = request.CustomHeaders;
        if (request.IsActive.HasValue)
            domain.IsActive = request.IsActive.Value;
        if (request.Protocol.HasValue)
            domain.ProxyProtocol = request.Protocol.Value;
        if (request.TargetPort.HasValue)
            domain.TargetPort = request.TargetPort.Value;

        DomainConfigurationHelper.NormalizeDomainForProtocol(domain);

        if (request.IsPrimary.HasValue && request.IsPrimary.Value && !domain.IsPrimary)
        {
            var otherDomains = await _domainRepository.GetByApplicationIdAsync(applicationId, cancellationToken);
            foreach (var d in otherDomains.Where(d => d.Id != id && d.IsPrimary))
            {
                d.IsPrimary = false;
                await _domainRepository.UpdateAsync(d, cancellationToken);
            }
            domain.IsPrimary = true;
        }

        await _domainRepository.UpdateAsync(domain, cancellationToken);
        _logger.LogInformation("Updated domain {DomainId} for application {AppId}", id, applicationId);

        await _traefikService.UpdateServiceLabelsAsync(applicationId, cancellationToken);

        if (domain.ProxyProtocol == ProxyProtocol.Tcp)
            await _proxyService.EnsureTcpEntrypointAsync(domain.Port, cancellationToken);

        return ApiActionResult<DomainDto>.Ok(_mapper.Map<DomainDto>(domain));
    }

    public async Task<ApiActionResult> DeleteDomainAsync(int applicationId, int id, CancellationToken cancellationToken)
    {
        var domain = await _domainRepository.GetByIdWithApplicationAsync(id, cancellationToken);

        if (domain == null || domain.ApplicationId != applicationId)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, "Domain not found");

        var wasPrimary = domain.IsPrimary;
        var app = domain.Application;

        await _domainRepository.DeleteAsync(domain, cancellationToken);

        if (wasPrimary)
        {
            var remainingDomains = await _domainRepository.GetByApplicationIdAsync(applicationId, cancellationToken);
            var newPrimary = remainingDomains.FirstOrDefault();

            if (newPrimary != null)
            {
                newPrimary.IsPrimary = true;
                await _domainRepository.UpdateAsync(newPrimary, cancellationToken);
            }
        }

        _logger.LogInformation("Deleted domain {DomainId} from application {AppId}", id, applicationId);

        try
        {
            await _proxyService.ConfigureApplicationAsync(app, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update proxy configuration after domain deletion");
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<DnsValidationResult>> ValidateDnsAsync(int applicationId, int id, CancellationToken cancellationToken)
    {
        var domain = await _domainRepository.GetByIdWithApplicationAsync(id, cancellationToken);

        if (domain == null || domain.ApplicationId != applicationId)
            return ApiActionResult<DnsValidationResult>.Fail(StatusCodes.Status404NotFound, "Domain not found");

        var coreResult = await _dnsValidationService.ValidateAsync(domain.Host, domain.Application.Server.Host, cancellationToken);

        var result = new DnsValidationResult
        {
            DomainId = id,
            Host = domain.Host,
            IsValid = coreResult.IsValid,
            Error = coreResult.Error,
            ExpectedIp = coreResult.ExpectedIp,
            ActualIp = coreResult.ActualIp,
            AllResolvedIps = coreResult.AllResolvedIps.ToList()
        };

        domain.DnsStatus = result.IsValid ? "valid" : "invalid";
        domain.DnsError = result.Error;
        domain.LastDnsCheck = DateTime.UtcNow;
        await _domainRepository.UpdateAsync(domain, cancellationToken);

        return ApiActionResult<DnsValidationResult>.Ok(result);
    }

    public async Task<ApiActionResult<IEnumerable<DnsValidationResult>>> ValidateAllDnsAsync(int applicationId, CancellationToken cancellationToken)
    {
        var domains = (await _domainRepository.GetByApplicationIdAsync(applicationId, cancellationToken)).ToList();
        var results = new List<DnsValidationResult>();

        if (domains.Any())
        {
            var firstDomain = await _domainRepository.GetByIdWithApplicationAsync(domains.First().Id, cancellationToken);
            if (firstDomain != null)
            {
                var serverHost = firstDomain.Application.Server.Host;

                foreach (var domain in domains)
                {
                    var coreResult = await _dnsValidationService.ValidateAsync(domain.Host, serverHost, cancellationToken);

                    var result = new DnsValidationResult
                    {
                        DomainId = domain.Id,
                        Host = domain.Host,
                        IsValid = coreResult.IsValid,
                        Error = coreResult.Error,
                        ExpectedIp = coreResult.ExpectedIp,
                        ActualIp = coreResult.ActualIp,
                        AllResolvedIps = coreResult.AllResolvedIps.ToList()
                    };

                    domain.DnsStatus = result.IsValid ? "valid" : "invalid";
                    domain.DnsError = result.Error;
                    domain.LastDnsCheck = DateTime.UtcNow;
                    await _domainRepository.UpdateAsync(domain, cancellationToken);

                    results.Add(result);
                }
            }
        }

        return ApiActionResult<IEnumerable<DnsValidationResult>>.Ok(results);
    }

    public async Task<ApiActionResult<List<DomainCertificateInfo>>> GetCertificatesAsync(int applicationId, CancellationToken cancellationToken)
    {
        var certificates = await _certificateRepository.GetByApplicationIdAsync(applicationId, cancellationToken);

        var response = certificates.Select(c => new DomainCertificateInfo
        {
            Id = c.Id,
            Domain = c.Domain,
            Provider = c.Provider,
            Status = c.Status,
            IssuedAt = c.IssuedAt,
            ExpiresAt = c.ExpiresAt,
            DaysUntilExpiry = c.ExpiresAt.HasValue
                ? (int)(c.ExpiresAt.Value - DateTime.UtcNow).TotalDays
                : 0,
            AutoRenew = c.AutoRenew,
            ErrorMessage = c.ErrorMessage
        }).ToList();

        return ApiActionResult<List<DomainCertificateInfo>>.Ok(response);
    }

    public async Task<ApiActionResult<RenewCertificateResponse>> RenewCertificateAsync(int applicationId, int certificateId, CancellationToken cancellationToken)
    {
        try
        {
            var success = await _certificateService.RenewCertificateAsync(certificateId);
            return ApiActionResult<RenewCertificateResponse>.Ok(new RenewCertificateResponse
            {
                Success = success,
                Message = success ? "Certificate renewed successfully" : "Failed to renew certificate"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew certificate {CertId}", certificateId);
            return ApiActionResult<RenewCertificateResponse>.Fail(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    public async Task<ApiActionResult> DeleteCertificateAsync(int applicationId, int certificateId, CancellationToken cancellationToken)
    {
        var certificate = await _certificateRepository.GetByIdAndApplicationAsync(certificateId, applicationId, cancellationToken);

        if (certificate == null)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, "Certificate not found");

        await _certificateRepository.DeleteAsync(certificate, cancellationToken);

        return ApiActionResult.Ok(StatusCodes.Status204NoContent);
    }
}
