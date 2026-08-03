using FluentAssertions;
using McpTrackTokens.Application.DTOs;
using McpTrackTokens.Application.Interfaces;
using McpTrackTokens.Application.Services;
using McpTrackTokens.Domain.Entities;
using NSubstitute;

namespace McpTrackTokens.Application.Tests;

public sealed class ApiKeyServiceTests
{
    private readonly IApiKeyRepository _repository = Substitute.For<IApiKeyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ApiKeyService CreateSut() => new(_repository, _unitOfWork);

    [Fact]
    public async Task CreateAsync_returns_plaintext_and_stores_hash_only()
    {
        TrackingApiKey? saved = null;
        _repository.AddAsync(Arg.Any<TrackingApiKey>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                saved = ci.Arg<TrackingApiKey>();
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        var result = await sut.CreateAsync(new CreateApiKeyRequestDto { Name = "local" });

        result.ApiKey.Should().StartWith("mtt_");
        saved.Should().NotBeNull();
        saved!.KeyHash.Should().Be(ApiKeyService.HashKey(result.ApiKey));
        saved.KeyHash.Should().NotBe(result.ApiKey);
        saved.Name.Should().Be("local");
    }

    [Fact]
    public async Task VerifyAsync_accepts_matching_plaintext_key()
    {
        var plaintext = "mtt_" + new string('a', 64);
        var hash = ApiKeyService.HashKey(plaintext);
        var entity = TrackingApiKey.Create("test", hash);

        _repository.FindByHashAsync(hash, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = CreateSut();
        var ok = await sut.VerifyAsync(plaintext);

        ok.Should().BeTrue();
        entity.LastUsedAtUtc.Should().NotBeNull();
        await _repository.Received(1).UpdateAsync(entity, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyAsync_rejects_unknown_or_inactive_key()
    {
        _repository.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((TrackingApiKey?)null);

        var sut = CreateSut();
        (await sut.VerifyAsync("mtt_unknown")).Should().BeFalse();
        (await sut.VerifyAsync("")).Should().BeFalse();
    }

    [Fact]
    public void HashKey_is_stable_sha256_hex()
    {
        var first = ApiKeyService.HashKey("mtt_sample");
        var second = ApiKeyService.HashKey("mtt_sample");
        first.Should().Be(second);
        first.Should().HaveLength(64);
        first.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public async Task RevokeAsync_rejects_revoking_last_active_key()
    {
        var entity = TrackingApiKey.Create("only", ApiKeyService.HashKey("mtt_only"));
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        _repository.ListAsync(true, Arg.Any<CancellationToken>())
            .Returns(new List<TrackingApiKey> { entity });

        var sut = CreateSut();
        var act = async () => await sut.RevokeAsync(entity.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*last active API key*");
        entity.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_removes_revoked_key()
    {
        var entity = TrackingApiKey.Create("old", ApiKeyService.HashKey("mtt_old"));
        entity.IsActive = false;
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = CreateSut();
        await sut.DeleteAsync(entity.Id);

        await _repository.Received(1).DeleteAsync(entity, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_rejects_active_key()
    {
        var entity = TrackingApiKey.Create("live", ApiKeyService.HashKey("mtt_live"));
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);

        var sut = CreateSut();
        var act = async () => await sut.DeleteAsync(entity.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Revoke*");
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<TrackingApiKey>(), Arg.Any<CancellationToken>());
    }
}
