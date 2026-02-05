import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { forkJoin, of } from 'rxjs';
import { catchError, finalize } from 'rxjs/operators';
import { MastersService, MasterItem } from 'app/core/masters/masters.service';
import { ProductsService, ProductItem } from 'app/core/products/products.service';
import { AlertRule, AlertsService } from 'app/core/alerts/alerts.service';
import {
    PriceSnapshotsService,
    LatestSnapshotResponse,
    SnapshotPrice,
    SnapshotRow,
    CompetitorInfo,
} from 'app/core/price-snapshots/price-snapshots.service';

interface ProductRowView {
    id: number;
    sku: string;
    ean: string | null;
    description: string;
    brandName: string | null;
    categoryName: string | null;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
    pricesByCompetitor: Record<number, SnapshotPrice>;
}

interface CompetitorSummary {
    competitor: CompetitorInfo;
    avgGapPercent: number | null;
    higherCount: number;
    promoCount: number;
}

interface TrendSeries {
    competitor: CompetitorInfo;
    values: Array<number | null>;
    points: string;
}

@Component({
    selector: 'app-dashboard',
    templateUrl: './dashboard.component.html',
    styleUrls: ['./dashboard.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        MatButtonModule,
    ],
})
export class DashboardComponent {
    private _mastersService = inject(MastersService);
    private _productsService = inject(ProductsService);
    private _snapshotsService = inject(PriceSnapshotsService);
    private _alertsService = inject(AlertsService);

    readonly loading = signal(false);
    readonly error = signal('');
    readonly snapshot = signal<LatestSnapshotResponse | null>(null);
    readonly products = signal<ProductItem[]>([]);
    readonly categories = signal<MasterItem[]>([]);
    readonly alertRules = signal<AlertRule[]>([]);
    readonly trendSeries = signal<TrendSeries[]>([]);
    readonly trendDates = signal<string[]>([]);

    readonly filterCategory = signal('');
    readonly filterSearch = signal('');

    readonly snapshotDate = computed(() => this.snapshot()?.snapshotDate ?? '—');
    readonly competitors = computed<CompetitorInfo[]>(
        () => this.snapshot()?.competitors ?? []
    );

    readonly rowsView = computed<ProductRowView[]>(() => {
        const snapshot = this.snapshot();
        const rowsByProductId = new Map<number, SnapshotRow>();
        for (const row of snapshot?.rows ?? []) {
            rowsByProductId.set(row.productId, row);
        }

        return this.products().map((product) => {
            const row = rowsByProductId.get(product.id);
            const pricesByCompetitor: Record<number, SnapshotPrice> = {};
            for (const price of row?.prices ?? []) {
                pricesByCompetitor[price.competitorId] = price;
            }

            return {
                id: product.id,
                sku: product.sku,
                ean: product.ean,
                description: product.description,
                brandName: product.brandName ?? row?.brandName ?? null,
                categoryName: product.categoryName ?? null,
                medipielListPrice:
                    row?.medipielListPrice ?? product.medipielListPrice ?? null,
                medipielPromoPrice:
                    row?.medipielPromoPrice ?? product.medipielPromoPrice ?? null,
                pricesByCompetitor,
            };
        });
    });

    readonly filteredRows = computed<ProductRowView[]>(() => {
        const category = this.normalize(this.filterCategory());
        const search = this.normalize(this.filterSearch());

        return this.rowsView().filter((row) => {
            if (category && !this.normalize(row.categoryName ?? '').includes(category)) {
                return false;
            }

            if (search) {
                const haystack = `${row.sku} ${row.ean ?? ''} ${row.description}`;
                if (!this.normalize(haystack).includes(search)) {
                    return false;
                }
            }

            return true;
        });
    });

    readonly totalProducts = computed(() => this.filteredRows().length);
    readonly noEanCount = computed(
        () => this.filteredRows().filter((row) => !row.ean).length
    );

    readonly matchCount = computed(() => {
        return this.filteredRows().filter((row) =>
            Object.values(row.pricesByCompetitor).some(
                (price) =>
                    price.listPrice !== null ||
                    price.promoPrice !== null ||
                    !!price.url
            )
        ).length;
    });

    readonly matchPercent = computed(() => {
        const total = this.totalProducts();
        return total > 0 ? (this.matchCount() / total) * 100 : 0;
    });

    readonly avgGapPercent = computed(() => {
        const values: number[] = [];
        for (const row of this.filteredRows()) {
            const base = row.medipielListPrice;
            if (!base) {
                continue;
            }
            for (const price of Object.values(row.pricesByCompetitor)) {
                if (price.listPrice === null || price.listPrice === undefined) {
                    continue;
                }
                values.push(((price.listPrice - base) / base) * 100);
            }
        }

        if (values.length === 0) {
            return null;
        }
        const total = values.reduce((acc, value) => acc + value, 0);
        return total / values.length;
    });

    readonly competitorSummaries = computed<CompetitorSummary[]>(() => {
        const rows = this.filteredRows();
        return this.competitors().map((competitor) => {
            const diffs: number[] = [];
            let higher = 0;
            let promos = 0;
            for (const row of rows) {
                const base = row.medipielListPrice;
                const price = row.pricesByCompetitor[competitor.id];
                if (!price || base === null || base === undefined) {
                    continue;
                }
                if (price.listPrice !== null && price.listPrice !== undefined) {
                    const diff = ((price.listPrice - base) / base) * 100;
                    diffs.push(diff);
                    if (price.listPrice > base) {
                        higher += 1;
                    }
                }
                if (
                    price.promoPrice !== null &&
                    price.promoPrice !== undefined &&
                    price.listPrice !== null &&
                    price.listPrice !== undefined &&
                    price.promoPrice < price.listPrice
                ) {
                    promos += 1;
                }
            }
            const avg =
                diffs.length > 0
                    ? diffs.reduce((acc, val) => acc + val, 0) / diffs.length
                    : null;
            return {
                competitor,
                avgGapPercent: avg,
                higherCount: higher,
                promoCount: promos,
            };
        });
    });

    readonly alertsSummary = computed(() => {
        const rows = this.filteredRows();
        const gapProducts = rows.filter((row) => {
            const base = row.medipielListPrice;
            if (!base) {
                return false;
            }
            const threshold = this.getThreshold(row.brandName, 'list');
            if (threshold === null) {
                return false;
            }
            return Object.values(row.pricesByCompetitor).some((price) => {
                if (price.listPrice === null || price.listPrice === undefined) {
                    return false;
                }
                const diff = ((price.listPrice - base) / base) * 100;
                return Math.abs(diff) >= threshold;
            });
        }).length;

        const noMatchByCompetitor = this.competitors().map((competitor) => {
            const count = rows.filter((row) => {
                const price = row.pricesByCompetitor[competitor.id];
                return !price || (price.listPrice === null && price.promoPrice === null);
            }).length;
            return { competitor, count };
        });

        return {
            gapProducts,
            noMatchByCompetitor,
        };
    });

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');
        forkJoin({
            categories: this._mastersService
                .getCategories()
                .pipe(catchError(() => of([]))),
            products: this._productsService
                .getProducts()
                .pipe(catchError(() => of([]))),
            snapshot: this._snapshotsService
                .getLatest(10000)
                .pipe(catchError(() => of(null))),
            rules: this._alertsService
                .getRules()
                .pipe(catchError(() => of([]))),
        })
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (result) => {
                    this.categories.set(result.categories);
                    this.products.set(result.products);
                    this.snapshot.set(result.snapshot);
                    this.alertRules.set(result.rules);
                    if (result.snapshot?.snapshotDate) {
                        this.loadTrend(result.snapshot.snapshotDate);
                    } else {
                        this.trendSeries.set([]);
                        this.trendDates.set([]);
                    }
                },
                error: () => {
                    this.error.set('No se pudo cargar el dashboard.');
                },
            });
    }

    private getThreshold(
        brandName: string | null,
        type: 'list' | 'promo'
    ): number | null {
        const defaultThreshold = 8;
        if (!brandName) {
            return defaultThreshold;
        }

        const rule = this.alertRules().find(
            (item) => item.brandName.toLowerCase() === brandName.toLowerCase()
        );

        if (!rule || !rule.active) {
            return defaultThreshold;
        }

        return type === 'list'
            ? rule.listPriceThresholdPercent ?? defaultThreshold
            : rule.promoPriceThresholdPercent ?? defaultThreshold;
    }

    resetFilters(): void {
        this.filterCategory.set('');
        this.filterSearch.set('');
    }

    private loadTrend(snapshotDate: string): void {
        const date = new Date(snapshotDate);
        if (Number.isNaN(date.getTime())) {
            this.trendSeries.set([]);
            this.trendDates.set([]);
            return;
        }

        const dates: string[] = [];
        for (let i = 6; i >= 0; i -= 1) {
            const d = new Date(date);
            d.setDate(d.getDate() - i);
            dates.push(d.toISOString().slice(0, 10));
        }

        const requests = dates.map((item) =>
            this._snapshotsService.getByDate(item, 10000).pipe(catchError(() => of(null)))
        );

        forkJoin(requests).subscribe((responses) => {
            const competitorMap = new Map<number, CompetitorInfo>();
            for (const competitor of this.competitors()) {
                competitorMap.set(competitor.id, competitor);
            }

            const seriesMap = new Map<number, Array<number | null>>();
            for (const competitor of this.competitors()) {
                seriesMap.set(competitor.id, []);
            }

            for (const response of responses) {
                if (!response) {
                    for (const competitor of this.competitors()) {
                        seriesMap.get(competitor.id)?.push(null);
                    }
                    continue;
                }

                for (const competitor of this.competitors()) {
                    const values: number[] = [];
                    for (const row of response.rows) {
                        const base = row.medipielListPrice;
                        if (!base) {
                            continue;
                        }
                        const price = row.prices.find((item) => item.competitorId === competitor.id);
                        if (!price || price.listPrice === null || price.listPrice === undefined) {
                            continue;
                        }
                        values.push(((price.listPrice - base) / base) * 100);
                    }
                    const avg =
                        values.length > 0
                            ? values.reduce((acc, val) => acc + val, 0) / values.length
                            : null;
                    seriesMap.get(competitor.id)?.push(avg);
                }
            }

            const allValues = Array.from(seriesMap.values())
                .flat()
                .filter((value): value is number => value !== null && value !== undefined);
            const min = allValues.length ? Math.min(...allValues) : -5;
            const max = allValues.length ? Math.max(...allValues) : 5;
            const normalizedMin = Math.min(min, -1);
            const normalizedMax = Math.max(max, 1);

            const chartWidth = 560;
            const chartHeight = 130;
            const chartPadding = 10;

            const series: TrendSeries[] = [];
            for (const [competitorId, values] of seriesMap.entries()) {
                const competitor = competitorMap.get(competitorId);
                if (!competitor) {
                    continue;
                }
                const points: string[] = [];
                values.forEach((value, index) => {
                    const x =
                        chartPadding +
                        (index / Math.max(values.length - 1, 1)) *
                            (chartWidth - chartPadding * 2);
                    const safeValue = value ?? 0;
                    const ratio =
                        (normalizedMax - safeValue) /
                        (normalizedMax - normalizedMin || 1);
                    const y = chartPadding + ratio * (chartHeight - chartPadding * 2);
                    points.push(`${x.toFixed(1)},${y.toFixed(1)}`);
                });
                series.push({
                    competitor,
                    values,
                    points: points.join(' '),
                });
            }

            this.trendDates.set(dates);
            this.trendSeries.set(series);
        });
    }

    private normalize(value: string): string {
        return value
            .normalize('NFD')
            .replace(/\p{Diacritic}/gu, '')
            .toLowerCase()
            .trim();
    }
}
