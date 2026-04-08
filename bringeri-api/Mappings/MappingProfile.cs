using AutoMapper;
using bringeri_api.DTOs.Auth;
using bringeri_api.DTOs.InvoiceBatches;
using bringeri_api.Entities;
using bringeri_api.Entities.Invoices;

namespace bringeri_api.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, AuthUserDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()))
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"))
            .ForMember(dest => dest.Role, opt => opt.MapFrom(src => src.Role.ToString().ToLowerInvariant()))
            .ForMember(dest => dest.TenantSlug, opt => opt.MapFrom(src => src.Tenant.Slug))
            .ForMember(dest => dest.TenantName, opt => opt.MapFrom(src => src.Tenant.Name));

        CreateMap<InvoiceLineItem, InvoiceLineItemDto>();

        CreateMap<InvoiceDocument, InvoiceEditorDto>()
            .ForMember(dest => dest.InvoiceId, opt => opt.MapFrom(src => src.Id.ToString()))
            .ForMember(dest => dest.FileId, opt => opt.MapFrom(src => src.Id.ToString()))
            .ForMember(dest => dest.FileName, opt => opt.MapFrom(src => src.OriginalFileName))
            .ForMember(dest => dest.RawAgentResponse, opt => opt.MapFrom(src => src.RawAgentResponse ?? string.Empty))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Status.ToString().ToLowerInvariant()))
            .ForMember(dest => dest.Issuer, opt => opt.MapFrom(src => new InvoicePartyDto
            {
                LegalName = src.IssuerLegalName,
                TaxId = src.IssuerTaxId,
                TaxStatus = src.IssuerTaxStatus,
            }))
            .ForMember(dest => dest.Recipient, opt => opt.MapFrom(src => new InvoicePartyDto
            {
                LegalName = src.RecipientLegalName,
                TaxId = src.RecipientTaxId,
                TaxStatus = string.Empty,
            }))
            .ForMember(dest => dest.Document, opt => opt.MapFrom(src => new InvoiceDocumentInfoDto
            {
                Type = src.DocumentType,
                PosNumber = src.PointOfSaleNumber,
                Number = src.DocumentNumber,
                IssueDate = src.IssueDate.HasValue ? src.IssueDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                FiscalAuthCode = src.FiscalAuthCode,
                FiscalAuthExpiry = src.FiscalAuthExpiry.HasValue ? src.FiscalAuthExpiry.Value.ToString("yyyy-MM-dd") : string.Empty,
            }))
            .ForMember(dest => dest.Totals, opt => opt.MapFrom(src => new InvoiceTotalsDto
            {
                Currency = src.Currency,
                NetSubtotal = src.NetSubtotal,
                Vat21 = src.Vat21,
                GrossIncomePerceptions = src.GrossIncomePerceptions,
                TotalAmount = src.TotalAmount,
            }))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items.OrderBy(item => item.SortOrder).ToList()));
    }
}
