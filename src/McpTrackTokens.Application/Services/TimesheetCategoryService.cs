using FluentValidation;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Domain.Entities;
using McpTrackTokens.Domain.Exceptions;
using DomainValidationException = McpTrackTokens.Domain.Exceptions.ValidationException;

namespace McpTrackTokens.Application.Services;

/// <summary>
/// CRUD for timesheet categories managed from Settings → Data.
/// </summary>
public sealed class TimesheetCategoryService : ITimesheetCategoryService
{
    private readonly ITimesheetCategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateTimesheetCategoryRequest> _createValidator;
    private readonly IValidator<UpdateTimesheetCategoryRequest> _updateValidator;

    public TimesheetCategoryService(
        ITimesheetCategoryRepository categories,
        IUnitOfWork unitOfWork,
        IValidator<CreateTimesheetCategoryRequest> createValidator,
        IValidator<UpdateTimesheetCategoryRequest> updateValidator)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TimesheetCategoryDto>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var list = await _categories.ListAsync(activeOnly, cancellationToken).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<TimesheetCategoryDto> CreateAsync(
        CreateTimesheetCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        if (await _categories.ExistsWithNameAsync(request.Name, excludingId: null, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new DomainValidationException(nameof(request.Name), "A category with this name already exists.");
        }

        var sortOrder = request.SortOrder ?? await NextSortOrderAsync(cancellationToken).ConfigureAwait(false);
        var category = TimesheetCategory.Create(request.Name, sortOrder);
        await _categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(category);
    }

    /// <inheritdoc />
    public async Task<TimesheetCategoryDto> UpdateAsync(
        Guid id,
        UpdateTimesheetCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);

        var category = await _categories.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TimesheetCategory), id);

        if (await _categories.ExistsWithNameAsync(request.Name, excludingId: id, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new DomainValidationException(nameof(request.Name), "A category with this name already exists.");
        }

        category.Rename(request.Name);
        category.SetSortOrder(request.SortOrder);
        category.SetActive(request.IsActive);
        await _categories.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(category);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new EntityNotFoundException(nameof(TimesheetCategory), id);

        var usage = await _categories.CountEntriesAsync(id, cancellationToken).ConfigureAwait(false);
        if (usage > 0)
        {
            // Soft-delete so historical timesheet rows keep a valid FK.
            category.SetActive(false);
            await _categories.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _categories.DeleteAsync(category, cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> NextSortOrderAsync(CancellationToken cancellationToken)
    {
        var list = await _categories.ListAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
        return list.Count == 0 ? 0 : list.Max(c => c.SortOrder) + 1;
    }

    private static TimesheetCategoryDto ToDto(TimesheetCategory category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        SortOrder = category.SortOrder,
        IsActive = category.IsActive,
        CreatedAtUtc = category.CreatedAtUtc,
        UpdatedAtUtc = category.UpdatedAtUtc
    };
}
