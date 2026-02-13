export interface CompetitorLike {
    id: number;
    name: string;
}

export function resolveBaselineCompetitorId(
    competitors: CompetitorLike[] | null | undefined
): number | null {
    if (!competitors || competitors.length === 0) {
        return null;
    }

    const baseline = competitors.find((item) =>
        (item.name ?? '').trim().toLowerCase().includes('medipiel')
    );

    return baseline?.id ?? competitors[0].id ?? null;
}

