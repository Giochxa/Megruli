using System.Text.Json;
using Megruli.Shared;
using Microsoft.JSInterop;

namespace Megruli.App.Services;

/// <summary>
/// Tracks XP/streak/hearts/mastery in browser localStorage — no backend, works fully offline.
/// </summary>
public class ProgressService
{
    private const string StorageKey = "megruli.progress";
    private readonly IJSRuntime _js;
    private UserProgress _progress = new();
    private bool _loaded;

    public event Action? OnChange;

    public ProgressService(IJSRuntime js)
    {
        _js = js;
    }

    public UserProgress Progress => _progress;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _progress = JsonSerializer.Deserialize<UserProgress>(json, JsonDefaults.Options) ?? new UserProgress();
            }
        }
        catch
        {
            _progress = new UserProgress();
        }
        TouchStreak();
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_progress, JsonDefaults.Options);
        await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        OnChange?.Invoke();
    }

    /// <summary>Bumps the streak if the last active day was yesterday, resets it if a day was missed, no-ops if already counted today.</summary>
    private void TouchStreak()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_progress.LastActiveDate == today) return;

        if (_progress.LastActiveDate == today.AddDays(-1))
        {
            _progress.Streak++;
        }
        else if (_progress.LastActiveDate is null)
        {
            _progress.Streak = 1;
        }
        else
        {
            _progress.Streak = 1; // missed a day (or more) — restart
        }

        _progress.LastActiveDate = today;
        _progress.Hearts = 5; // daily heart refill
    }

    public async Task AddXpAsync(int amount)
    {
        await EnsureLoadedAsync();
        _progress.Xp += amount;
        await SaveAsync();
    }

    public async Task LoseHeartAsync()
    {
        await EnsureLoadedAsync();
        if (_progress.UnlimitedHearts) return;
        if (_progress.Hearts > 0) _progress.Hearts--;
        await SaveAsync();
    }

    public async Task SetUnlimitedHeartsAsync(bool enabled)
    {
        await EnsureLoadedAsync();
        _progress.UnlimitedHearts = enabled;
        await SaveAsync();
    }

    public async Task CompleteLessonAsync(string lessonId, int xpAward)
    {
        await EnsureLoadedAsync();
        _progress.CompletedLessonIds.Add(lessonId);
        _progress.Xp += xpAward;
        await SaveAsync();
    }

    /// <summary>Simple Leitner-box spaced repetition: correct answers move a word up a box, wrong answers send it back to box 0.</summary>
    public async Task RecordAnswerAsync(string wordId, bool correct)
    {
        await EnsureLoadedAsync();
        if (!_progress.Mastery.TryGetValue(wordId, out var mastery))
        {
            mastery = new WordMastery();
            _progress.Mastery[wordId] = mastery;
        }
        mastery.LastReviewed = DateTime.Now;
        if (correct)
        {
            mastery.TimesCorrect++;
            mastery.Box = Math.Min(5, mastery.Box + 1);
        }
        else
        {
            mastery.TimesWrong++;
            mastery.Box = 0;
        }
        await SaveAsync();
    }

    public bool IsLessonCompleted(string lessonId) => _progress.CompletedLessonIds.Contains(lessonId);

    /// <summary>Words due for review, weighted toward low-mastery/never-seen words — used by the Practice hub.</summary>
    public List<string> GetWeakWordIds(IEnumerable<string> candidateIds, int count)
    {
        return candidateIds
            .OrderBy(id => _progress.Mastery.TryGetValue(id, out var m) ? m.Box : -1)
            .ThenBy(_ => Guid.NewGuid())
            .Take(count)
            .ToList();
    }
}
