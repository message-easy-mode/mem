using System.Collections.Concurrent;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqBootstrapReviewSnapshot(
    string ReviewId,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool AcceptEula,
    string? PrivateUiUrl,
    int SelectedHostPort,
    string ApprovedImageReference,
    string ExpectedVersion,
    string StorageState,
    string RuntimeOwnershipState,
    bool RuntimeExists,
    bool RuntimeManaged,
    bool AdministratorPasswordRequired,
    bool EnableEventDelivery = true,
    bool CurrentAdministratorPasswordRequired = false);

/// <summary>
/// Process-local, secret-free review cache. Reviews are short-lived and are
/// always revalidated against current Docker, storage and image state before
/// execution. Nothing in this store is an authority or a credential.
/// </summary>
public sealed class SeqBootstrapReviewStore(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, SeqBootstrapReviewSnapshot> _reviews =
        new(StringComparer.Ordinal);

    public void Store(SeqBootstrapReviewSnapshot review)
    {
        ArgumentNullException.ThrowIfNull(review);
        RemoveExpired();
        _reviews[review.ReviewId] = review;
    }

    public SeqBootstrapReviewSnapshot GetRequired(string reviewId)
    {
        if (string.IsNullOrWhiteSpace(reviewId) ||
            !_reviews.TryGetValue(reviewId.Trim(), out var review))
        {
            throw new SeqOperationException(
                "seq_bootstrap_review_not_found",
                Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict,
                "The reviewed Seq setup could not be found. Review setup again before continuing.");
        }

        if (review.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            _reviews.TryRemove(review.ReviewId, out _);
            throw new SeqOperationException(
                "seq_bootstrap_review_expired",
                Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict,
                "The reviewed Seq setup has expired. Review setup again before continuing.");
        }

        return review;
    }

    public void Remove(string reviewId)
    {
        if (!string.IsNullOrWhiteSpace(reviewId))
        {
            _reviews.TryRemove(reviewId.Trim(), out _);
        }
    }

    private void RemoveExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var item in _reviews)
        {
            if (item.Value.ExpiresAtUtc <= now)
            {
                _reviews.TryRemove(item.Key, out _);
            }
        }
    }
}
