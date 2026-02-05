import {
    ChangeDetectionStrategy,
    Component,
    computed,
    inject,
    signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { finalize } from 'rxjs/operators';
import {
    LatestSnapshotResponse,
    SnapshotPrice,
    SnapshotRow,
    PriceSnapshotsService,
    CompetitorInfo,
} from 'app/core/price-snapshots/price-snapshots.service';
import { AlertRule, AlertsService } from 'app/core/alerts/alerts.service';
import { MasterItem, MastersService } from 'app/core/masters/masters.service';
import { ReportsService } from 'app/core/reports/reports.service';

interface CompetitorDelta {
    amount: number | null;
    percent: number | null;
}

interface SnapshotRowView extends SnapshotRow {
    pricesByCompetitor: Record<number, SnapshotPrice>;
    deltaListByCompetitor: Record<number, CompetitorDelta>;
    deltaPromoByCompetitor: Record<number, CompetitorDelta>;
}

@Component({
    selector: 'app-snapshots-history',
    templateUrl: './snapshots-history.component.html',
    styleUrls: ['./snapshots-history.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        MatButtonModule,
        MatIconModule,
        MatTableModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
    ],
})
export class SnapshotsHistoryComponent {
    private _service = inject(PriceSnapshotsService);
    private _alertsService = inject(AlertsService);
    private _mastersService = inject(MastersService);
    private _reportsService = inject(ReportsService);

    readonly loading = signal(false);
    readonly error = signal('');
    readonly data = signal<LatestSnapshotResponse | null>(null);
    readonly selectedDate = signal('');
    readonly alertRules = signal<AlertRule[]>([]);
    readonly brands = signal<MasterItem[]>([]);
    readonly categories = signal<MasterItem[]>([]);
    readonly reportFrom = signal('');
    readonly reportTo = signal('');
    readonly reportBrandId = signal<number | null>(null);
    readonly reportCategoryId = signal<number | null>(null);
    readonly exporting = signal(false);
    readonly exportError = signal('');

    readonly competitors = computed<CompetitorInfo[]>(
        () => this.data()?.competitors ?? []
    );
    readonly rows = computed<SnapshotRow[]>(() => this.data()?.rows ?? []);
    readonly rowsView = computed<SnapshotRowView[]>(() =>
        this.rows().map((row) => {
            const pricesByCompetitor: Record<number, SnapshotPrice> = {};
            const deltaListByCompetitor: Record<number, CompetitorDelta> = {};
            const deltaPromoByCompetitor: Record<number, CompetitorDelta> = {};
            for (const price of row.prices) {
                pricesByCompetitor[price.competitorId] = price;
            }

            const baseListPrice = row.medipielListPrice ?? null;
            const basePromoPrice = row.medipielPromoPrice ?? null;

            for (const competitor of this.competitors()) {
                const price = pricesByCompetitor[competitor.id];
                const listPrice = price?.listPrice;
                const promoPrice = price?.promoPrice;

                if (!price || baseListPrice === null || listPrice === null || listPrice === undefined) {
                    deltaListByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                } else {
                    const amount = listPrice - baseListPrice;
                    const percent =
                        baseListPrice !== 0 ? (amount / baseListPrice) * 100 : null;
                    deltaListByCompetitor[competitor.id] = {
                        amount,
                        percent,
                    };
                }

                if (!price || basePromoPrice === null || promoPrice === null || promoPrice === undefined) {
                    deltaPromoByCompetitor[competitor.id] = {
                        amount: null,
                        percent: null,
                    };
                } else {
                    const amount = promoPrice - basePromoPrice;
                    const percent =
                        basePromoPrice !== 0 ? (amount / basePromoPrice) * 100 : null;
                    deltaPromoByCompetitor[competitor.id] = {
                        amount,
                        percent,
                    };
                }
            }

            return {
                ...row,
                pricesByCompetitor,
                deltaListByCompetitor,
                deltaPromoByCompetitor,
            };
        })
    );
    readonly hasItems = computed(() => this.rowsView().length > 0);
    readonly snapshotDate = computed(() => this.data()?.snapshotDate ?? '—');
    readonly colorPalette = [
        '#729fcf',
        '#ffd9b3',
        '#b7d36b',
        '#f3a3a3',
        '#a1c9f1',
    ];

    constructor() {
        this.loadLatest();
        this.loadAlertRules();
        this.loadMasters();
    }

    loadLatest(): void {
        this.loading.set(true);
        this.error.set('');

        this._service
            .getLatest()
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (data) => {
                    this.data.set(data);
                    if (data.snapshotDate) {
                        this.selectedDate.set(data.snapshotDate);
                    }
                },
                error: () => this.error.set('No se pudo cargar el ultimo snapshot.'),
            });
    }

    loadMasters(): void {
        this._mastersService.getBrands().subscribe({
            next: (items) => this.brands.set(items),
            error: () => this.brands.set([]),
        });
        this._mastersService.getCategories().subscribe({
            next: (items) => this.categories.set(items),
            error: () => this.categories.set([]),
        });
    }

    exportExcel(): void {
        const from = this.reportFrom() || this.selectedDate();
        const to = this.reportTo() || from;

        if (!from) {
            this.exportError.set('Selecciona un rango de fechas para exportar.');
            return;
        }

        this.exporting.set(true);
        this.exportError.set('');

        this._reportsService
            .downloadExcel({
                from,
                to,
                brandId: this.reportBrandId(),
                categoryId: this.reportCategoryId(),
            })
            .pipe(finalize(() => this.exporting.set(false)))
            .subscribe({
                next: (blob) => {
                    const url = window.URL.createObjectURL(blob);
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `reporte_precios_${from}_${to}.xlsx`;
                    link.click();
                    window.setTimeout(() => {
                        window.URL.revokeObjectURL(url);
                    }, 0);
                },
                error: () => {
                    this.exportError.set('No se pudo exportar el reporte.');
                },
            });
    }

    loadAlertRules(): void {
        this._alertsService.getRules().subscribe({
            next: (rules) => this.alertRules.set(rules),
            error: () => this.alertRules.set([]),
        });
    }

    onDateChange(event: Event): void {
        const value = (event.target as HTMLInputElement).value;
        this.selectedDate.set(value);
    }

    loadByDate(): void {
        const date = this.selectedDate();
        if (!date) {
            this.error.set('Selecciona una fecha para consultar.');
            return;
        }

        this.loading.set(true);
        this.error.set('');

        this._service
            .getByDate(date)
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (data) => this.data.set(data),
                error: () => this.error.set('No se pudo cargar el snapshot.'),
            });
    }

    getCompetitorColor(index: number, competitor: CompetitorInfo): string {
        return competitor.color ?? this.colorPalette[index % this.colorPalette.length];
    }

    isLargeDelta(
        delta: CompetitorDelta | undefined | null,
        brandName: string | null,
        type: 'list' | 'promo'
    ): boolean {
        if (!delta || delta.percent === null || delta.percent === undefined) {
            return false;
        }
        const threshold = this.getThreshold(brandName, type);
        if (threshold === null) {
            return false;
        }
        return Math.abs(delta.percent) >= threshold;
    }

    private getThreshold(
        brandName: string | null,
        type: 'list' | 'promo'
    ): number | null {
        const defaultThreshold = 30;
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
}
