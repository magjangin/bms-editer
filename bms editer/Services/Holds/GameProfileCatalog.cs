using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using bms_editer.Models;

namespace bms_editer.Services.Holds;

// 게임 프로파일 목록.
//
// 기본 프로파일은 exe 안에 들어 있다(EmbeddedResource). README 가 "exe 하나만 복사하면 실행"을
// 약속하므로, 옆 폴더에만 두면 exe 만 복사해 간 사용자는 홀드가 전혀 안 잡히는데 이유가 화면에 없다.
// exe 옆에 profiles 폴더가 있으면 같은 id 를 덮어쓴다. 리빌드 없이 게임을 추가하거나 고칠 수 있다.
public sealed class GameProfileCatalog
{
    private const string ResourcePrefix = "profiles/";

    private static readonly Lazy<GameProfileCatalog> DefaultCatalog =
        new(() => Load(Path.Combine(AppContext.BaseDirectory, "profiles")));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static GameProfileCatalog Default => DefaultCatalog.Value;

    public IReadOnlyList<GameProfile> Profiles { get; }

    // 읽지 못한 프로파일과 그 이유. 조용히 빠지면 "왜 이 게임이 목록에 없지"를 알 길이 없다.
    public IReadOnlyList<string> LoadErrors { get; }

    private GameProfileCatalog(IReadOnlyList<GameProfile> profiles, IReadOnlyList<string> loadErrors)
    {
        Profiles = profiles;
        LoadErrors = loadErrors;
    }

    public static GameProfileCatalog Load(string? overrideDirectory)
    {
        var byId = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        // 어떤 예외든 그 파일 하나만 빼고 넘어간다. 사용자가 손으로 고친 파일 하나 때문에
        // 앱이 켜지지 않으면 안 된다. (예전에는 null 한 칸이 NullReferenceException 으로 새어 나가
        // 메인 뷰모델을 만드는 자리에서 앱 시작이 통째로 실패했다)
        void TryAdd(string name, string json)
        {
            try
            {
                var profile = Parse(json);
                byId[profile.Id] = profile;
            }
            catch (Exception ex)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        foreach (var (name, json) in ReadEmbedded())
            TryAdd(name, json);

        if (!string.IsNullOrEmpty(overrideDirectory) && Directory.Exists(overrideDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(overrideDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    TryAdd(file, File.ReadAllText(file, Encoding.UTF8));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{file}: {ex.Message}");
                }
            }
        }

        var profiles = byId.Values
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCulture)
            .ToArray();

        return new GameProfileCatalog(profiles, errors);
    }

    public static IEnumerable<(string Name, string Json)> ReadEmbedded()
    {
        var assembly = typeof(GameProfileCatalog).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return (name, reader.ReadToEnd());
        }
    }

    public static GameProfile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<GameProfile>(json, JsonOptions)
            ?? throw new FormatException("프로파일이 비어 있습니다.");

        Validate(profile);
        return profile;
    }

    // 규칙이 빠진 프로파일은 "홀드가 하나도 안 잡히는" 조용한 실패로 이어진다. 읽을 때 막는다.
    private static void Validate(GameProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new FormatException("id 가 없습니다.");

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = profile.Id;

        FillMissing(profile);

        if (profile.HoldRules.Count == 0)
            throw new FormatException($"{profile.Id}: holdRules 가 비어 있습니다.");

        // JSON 에 숫자로 적으면 열거형에 없는 값도 그대로 들어온다. 짝 엔진은 그런 정책을 조용히 건너뛴다.
        if (!Enum.IsDefined(profile.Verification))
            throw new FormatException($"{profile.Id}: verification 값 {(int)profile.Verification} 을 모릅니다.");

        foreach (var rule in profile.HoldRules)
        {
            var role = rule.Role;
            var where = $"{profile.Id}/{rule.Kind}";

            if (!Enum.IsDefined(role.Type) || !Enum.IsDefined(rule.Policy) || !Enum.IsDefined(rule.Scope))
                throw new FormatException($"{where}: type·policy·scope 중 모르는 값이 있습니다.");

            switch (role.Type)
            {
                case HoldRoleSource.KeyValue when role.HeadKeys.Count == 0 || role.TailKeys.Count == 0:
                    throw new FormatException($"{where}: KeyValue 는 headKeys 와 tailKeys 가 모두 있어야 합니다.");
                case HoldRoleSource.FileNameKeyword when role.HeadKeywords.Count == 0 || role.TailKeywords.Count == 0:
                    throw new FormatException($"{where}: FileNameKeyword 는 headKeywords 와 tailKeywords 가 모두 있어야 합니다.");
                case HoldRoleSource.FileNameUid when profile.UidTable is null:
                    throw new FormatException($"{where}: FileNameUid 는 프로파일에 uidTable 이 있어야 합니다.");
                case HoldRoleSource.FileNameBase when role.BaseNames.Count == 0:
                    throw new FormatException($"{where}: FileNameBase 는 baseNames 가 있어야 합니다.");
            }

            // 순서로 짝짓는 규칙은 시작/끝을 따로 고르지 않으므로 Alternate 와만 맞는다. 반대도 마찬가지다.
            var ordersByOccurrence = role.Type is HoldRoleSource.FileNameUid or HoldRoleSource.FileNameBase;
            if (ordersByOccurrence != (rule.Policy == HoldPairingPolicy.Alternate))
                throw new FormatException($"{where}: {role.Type} 와 {rule.Policy} 는 함께 쓸 수 없습니다.");
        }

        // 잘못된 정규식은 읽을 때가 아니라 짝을 읽을 때(편집할 때마다) 터진다. 여기서 먼저 막는다.
        if (profile.UidTable is { } table)
        {
            try
            {
                _ = new Regex(table.Pattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"{profile.Id}: uidTable.pattern 이 올바른 정규식이 아닙니다. {ex.Message}");
            }
        }
    }

    // JSON 에 null 을 적으면 초기값 대신 null 이 들어온다. 짝 엔진과 분류기는 목록과 글자가
    // 늘 있다고 보므로, 없는 것은 빈 값으로 채우고 채울 수 없는 것(빈 규칙·빈 역할)은 거부한다.
    private static void FillMissing(GameProfile profile)
    {
        profile.Source ??= "";
        profile.Note ??= "";
        profile.PathHints = WithoutNulls(profile.PathHints);
        profile.HoldRules ??= new();

        foreach (var rule in profile.HoldRules)
        {
            if (rule is null)
                throw new FormatException($"{profile.Id}: holdRules 에 빈 항목(null)이 있습니다.");
            if (string.IsNullOrWhiteSpace(rule.Kind))
                throw new FormatException($"{profile.Id}: kind 가 없는 규칙이 있습니다.");
            if (rule.Role is null)
                throw new FormatException($"{profile.Id}/{rule.Kind}: role 이 없습니다.");

            rule.Channels = WithoutNulls(rule.Channels);
            rule.OrphanHeadOutcome ??= "";
            rule.OrphanTailOutcome ??= "";

            var role = rule.Role;
            role.HeadKeys = WithoutNulls(role.HeadKeys);
            role.TailKeys = WithoutNulls(role.TailKeys);
            role.RequiredKeyword ??= "";
            role.HeadKeywords = WithoutNulls(role.HeadKeywords);
            role.TailKeywords = WithoutNulls(role.TailKeywords);
            role.BaseNames = WithoutNulls(role.BaseNames);
            role.StripSuffixes = WithoutNulls(role.StripSuffixes);
            role.HeadLabels = WithoutNulls(role.HeadLabels);
            role.TailLabels = WithoutNulls(role.TailLabels);
        }

        if (profile.UidTable is not { } table)
            return;

        if (string.IsNullOrEmpty(table.Pattern))
            throw new FormatException($"{profile.Id}: uidTable.pattern 이 비어 있습니다.");

        table.PrefixTypes ??= new();
        table.TypeCodes ??= new();
        table.ResolvedWideCodes = WithoutNulls(table.ResolvedWideCodes);
        table.ResolvedTypeCodes = WithoutNulls(table.ResolvedTypeCodes);
        table.FinalPrefixes ??= new();
        table.BlockingKeywords = WithoutNulls(table.BlockingKeywords);
        table.BlockingType ??= "";
        table.NameFallbacks = table.NameFallbacks?.Where(fallback => fallback is not null).ToList() ?? new();

        foreach (var fallback in table.NameFallbacks)
        {
            fallback.Keywords = WithoutNulls(fallback.Keywords);
            fallback.UidPrefix ??= "";
            fallback.UidTypeCode ??= "";
            fallback.Type ??= "";
        }
    }

    private static List<string> WithoutNulls(List<string>? values) =>
        values?.Where(value => value is not null).ToList() ?? new();

    public GameProfile? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    // 차트 경로로 게임을 추정한다.
    //
    // 실제 게임 차트는 게임 폴더 아래 hwa\<곡>\hwa2.bms 에 있어서, 곡 폴더가 아니라
    // 그 위의 게임 폴더 이름이 단서가 된다. 그래서 차트가 든 폴더(곡 폴더)는 **맨 나중에** 본다.
    // 예전에는 곡 폴더부터 봐서, DEFLATE 폴더 안의 "Stargazer" 라는 곡이 스타게이저로 추정됐다.
    // 곡 제목에는 게임 이름과 같은 낱말이 흔하다.
    //
    // 그 위 폴더들은 가까운 것부터 보고, 한 폴더에 여러 힌트가 걸리면 긴 힌트(더 구체적인 이름)를 따른다.
    public GameProfile? DetectFromPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var segments = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var chartFolder = segments.Length - 2;

        for (var i = chartFolder - 1; i >= 0; i--)
        {
            if (FindByPathHint(segments[i]) is { } found)
                return found;
        }

        return chartFolder >= 0 ? FindByPathHint(segments[chartFolder]) : null;
    }

    private GameProfile? FindByPathHint(string folderName)
    {
        GameProfile? best = null;
        var bestLength = 0;

        foreach (var profile in Profiles)
        {
            foreach (var hint in profile.PathHints)
            {
                if (hint.Length > bestLength && folderName.Contains(hint, StringComparison.OrdinalIgnoreCase))
                {
                    best = profile;
                    bestLength = hint.Length;
                }
            }
        }

        return best;
    }
}
