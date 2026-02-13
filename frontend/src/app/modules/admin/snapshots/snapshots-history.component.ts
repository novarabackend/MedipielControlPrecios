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
import { resolveBaselineCompetitorId } from 'app/core/competitors/competitor-utils';
import { AlertRule, AlertsService } from 'app/core/alerts/alerts.service';
import { MasterItem, MastersService } from 'app/core/masters/masters.service';
import { ReportsService } from 'app/core/reports/reports.service';
import {
    ProductCompetitorPrice,
    ProductDetailResponse,
    ProductsService,
} from 'app/core/products/products.service';

interface CompetitorDelta {
    amount: number | null;
    percent: number | null;
}

interface SnapshotRowView extends SnapshotRow {
    pricesByCompetitor: Record<number, SnapshotPrice>;
    deltaListByCompetitor: Record<number, CompetitorDelta>;
    deltaPromoByCompetitor: Record<number, CompetitorDelta>;
}

interface EditorCompetitorRow {
    competitor: CompetitorInfo;
    latest: ProductCompetitorPrice | null;
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
    private _productsService = inject(ProductsService);

    readonly loading = signal(false);
    readonly error = signal('');
    readonly data = signal<LatestSnapshotResponse | null>(null);
    readonly selectedDate = signal('');
    readonly alertRules = signal<AlertRule[]>([]);
    readonly brands = signal<MasterItem[]>([]);
    readonly suppliers = signal<MasterItem[]>([]);
    readonly categories = signal<MasterItem[]>([]);
    readonly lines = signal<MasterItem[]>([]);
    readonly reportFrom = signal('');
    readonly reportTo = signal('');
    readonly exporting = signal(false);
    readonly exportError = signal('');
    readonly filterEan = signal('');
    readonly filterName = signal('');
    readonly filterBrandId = signal<number | null>(null);
    readonly filterSupplierId = signal<number | null>(null);
    readonly filterCategoryId = signal<number | null>(null);
    readonly filterLineId = signal<number | null>(null);
    readonly filterCompetitor = signal<number | null>(null);
    readonly filterStatus = signal('all');
    readonly editorOpen = signal(false);
    readonly editorLoading = signal(false);
    readonly editorError = signal('');
    readonly editorProductId = signal<number | null>(null);
    readonly editorData = signal<ProductDetailResponse | null>(null);
    readonly editorUrlDrafts = signal<Record<number, string>>({});
    readonly editorSavingIds = signal<Set<number>>(new Set());

    readonly competitors = computed<CompetitorInfo[]>(
        () => this.data()?.competitors ?? []
    );
    readonly baselineCompetitorId = computed<number | null>(() =>
        resolveBaselineCompetitorId(this.competitors())
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

            const baseline = this.baselineCompetitorId();
            const baselinePrice = baseline ? pricesByCompetitor[baseline] : undefined;
            const baseListPrice = baselinePrice?.listPrice ?? null;
            const basePromoPrice = baselinePrice?.promoPrice ?? null;

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
    readonly filteredRowsView = computed<SnapshotRowView[]>(() => {
        const eanFilter = this.normalize(this.filterEan());
        const nameFilter = this.normalize(this.filterName());
        const brandIdFilter = this.filterBrandId();
        const supplierIdFilter = this.filterSupplierId();
        const categoryIdFilter = this.filterCategoryId();
        const lineIdFilter = this.filterLineId();
        const competitorFilter = this.filterCompetitor();
        const statusFilter = this.filterStatus();

        return this.rowsView().filter((row) => {
            if (eanFilter && !this.normalize(row.ean ?? '').includes(eanFilter)) {
                return false;
            }

            if (
                nameFilter &&
                !this.normalize(row.description ?? '').includes(nameFilter)
            ) {
                return false;
            }

            if (
                brandIdFilter !== null &&
                brandIdFilter !== undefined &&
                row.brandId !== brandIdFilter
            ) {
                return false;
            }

            if (
                supplierIdFilter !== null &&
                supplierIdFilter !== undefined &&
                row.supplierId !== supplierIdFilter
            ) {
                return false;
            }

            if (
                categoryIdFilter !== null &&
                categoryIdFilter !== undefined &&
                row.categoryId !== categoryIdFilter
            ) {
                return false;
            }

            if (
                lineIdFilter !== null &&
                lineIdFilter !== undefined &&
                row.lineId !== lineIdFilter
            ) {
                return false;
            }

            if (statusFilter === 'no-ean') {
                return !row.ean;
            }

            if (statusFilter === 'matched') {
                return this.hasMatch(row, competitorFilter);
            }

            if (statusFilter === 'unmatched') {
                return !this.hasMatch(row, competitorFilter);
            }

            return true;
        });
    });
    readonly totalCount = computed(() => this.rowsView().length);
    readonly filteredCount = computed(() => this.filteredRowsView().length);
    readonly hasRows = computed(() => this.totalCount() > 0);
    readonly hasItems = computed(() => this.filteredCount() > 0);
    readonly snapshotDate = computed(() => this.data()?.snapshotDate ?? '—');
    readonly editorRows = computed<EditorCompetitorRow[]>(() => {
        const data = this.editorData();
        if (!data) {
            return [];
        }

        const latestByCompetitor = new Map<number, ProductCompetitorPrice>();
        for (const item of data.latest) {
            latestByCompetitor.set(item.competitorId, item);
        }

        return data.competitors.map((competitor) => ({
            competitor,
            latest: latestByCompetitor.get(competitor.id) ?? null,
        }));
    });
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
        this._mastersService.getSuppliers().subscribe({
            next: (items) => this.suppliers.set(items),
            error: () => this.suppliers.set([]),
        });
        this._mastersService.getCategories().subscribe({
            next: (items) => this.categories.set(items),
            error: () => this.categories.set([]),
        });
        this._mastersService.getLines().subscribe({
            next: (items) => this.lines.set(items),
            error: () => this.lines.set([]),
        });
    }

    resetFilters(): void {
        this.filterEan.set('');
        this.filterName.set('');
        this.filterBrandId.set(null);
        this.filterSupplierId.set(null);
        this.filterCategoryId.set(null);
        this.filterLineId.set(null);
        this.filterCompetitor.set(null);
        this.filterStatus.set('all');
    }

    exportExcel(): void {
        const from = this.reportFrom() || this.selectedDate();
        const to = this.reportTo() || from;

        if (!from) {
            this.exportError.set('Selecciona un rango de fechas para exportar.');
            return;
        }

        const productIds = this.filteredRowsView().map((item) => item.productId);
        if (productIds.length === 0) {
            this.exportError.set('No hay productos para exportar.');
            return;
        }

        this.exporting.set(true);
        this.exportError.set('');

        this._reportsService
            .downloadExcel({
                from,
                to,
                brandId: this.filterBrandId(),
                categoryId: this.filterCategoryId(),
                productIds,
                format: 'long',
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

    hasMatch(row: SnapshotRowView, competitorId: number | null): boolean {
        if (!competitorId) {
            return Object.values(row.pricesByCompetitor).some(
                (price) =>
                    price.listPrice !== null ||
                    price.promoPrice !== null ||
                    !!price.url
            );
        }

        const price = row.pricesByCompetitor[competitorId];
        if (!price) {
            return false;
        }

        return price.listPrice !== null || price.promoPrice !== null || !!price.url;
    }

    getCompetitorColor(index: number, competitor: CompetitorInfo): string {
        return competitor.color ?? this.colorPalette[index % this.colorPalette.length];
    }

    getMatchMethodLabel(method: string | null | undefined): string {
        const value = (method ?? '').trim().toLowerCase();
        switch (value) {
            case 'ean':
                return 'Match: EAN';
            case 'name':
                return 'Match: Nombre/descripcion';
            case 'ai':
                return 'Match: IA';
            case 'manual':
                return 'Match: Manual';
            case 'no_match':
                return 'Match: Sin match';
            case 'retry':
                return 'Match: Reintento';
            default:
                return 'Match: —';
        }
    }

    openQuickEdit(row: SnapshotRow): void {
        this.editorOpen.set(true);
        this.editorLoading.set(true);
        this.editorError.set('');
        this.editorProductId.set(row.productId);
        this.editorData.set(null);
        this.editorUrlDrafts.set({});
        this.editorSavingIds.set(new Set());

        this._productsService
            .getProductDetail(row.productId, 7)
            .pipe(finalize(() => this.editorLoading.set(false)))
            .subscribe({
                next: (data) => {
                    this.editorData.set(data);
                    this.syncEditorUrlDrafts(data);
                },
                error: () =>
                    this.editorError.set('No se pudo cargar el detalle del producto.'),
            });
    }

    closeQuickEdit(): void {
        this.editorOpen.set(false);
        this.editorLoading.set(false);
        this.editorError.set('');
        this.editorProductId.set(null);
        this.editorData.set(null);
        this.editorUrlDrafts.set({});
        this.editorSavingIds.set(new Set());
    }

    onEditorUrlChange(competitorId: number, value: string): void {
        const next = { ...this.editorUrlDrafts() };
        next[competitorId] = value;
        this.editorUrlDrafts.set(next);
    }

    getEditorUrlDraft(competitorId: number, fallback: string | null): string {
        return this.editorUrlDrafts()[competitorId] ?? fallback ?? '';
    }

    isEditorSaving(competitorId: number): boolean {
        return this.editorSavingIds().has(competitorId);
    }

    saveEditorUrl(competitorId: number): void {
        const productId = this.editorProductId();
        if (!productId) {
            return;
        }

        const url = (this.editorUrlDrafts()[competitorId] ?? '').trim();
        if (!url) {
            this.editorError.set('Ingresa una URL valida para guardar.');
            return;
        }

        this.setEditorSaving(competitorId, true);
        this._productsService
            .updateCompetitorUrl(productId, competitorId, url)
            .pipe(finalize(() => this.setEditorSaving(competitorId, false)))
            .subscribe({
                next: (updated) => {
                    this.editorError.set('');
                    this.editorData.update((current) => {
                        if (!current) {
                            return current;
                        }

                        const latest = current.latest.map((item) =>
                            item.competitorId === competitorId
                                ? {
                                      ...item,
                                      url: updated.url,
                                      matchMethod: updated.matchMethod,
                                      matchScore: updated.matchScore,
                                      lastMatchedAt: updated.lastMatchedAt,
                                  }
                                : item
                        );

                        return {
                            ...current,
                            latest,
                        };
                    });

                    this.loadByDate();
                },
                error: () =>
                    this.editorError.set('No se pudo guardar la URL.'),
            });
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

    private setEditorSaving(competitorId: number, saving: boolean): void {
        const next = new Set(this.editorSavingIds());
        if (saving) {
            next.add(competitorId);
        } else {
            next.delete(competitorId);
        }
        this.editorSavingIds.set(next);
    }

    private syncEditorUrlDrafts(data: ProductDetailResponse): void {
        const latestByCompetitor = new Map<number, ProductCompetitorPrice>();
        for (const item of data.latest) {
            latestByCompetitor.set(item.competitorId, item);
        }

        const draft: Record<number, string> = {};
        for (const competitor of data.competitors) {
            draft[competitor.id] = latestByCompetitor.get(competitor.id)?.url ?? '';
        }
        this.editorUrlDrafts.set(draft);
    }

    private normalize(value: string): string {
        return value
            .normalize('NFD')
            .replace(/\p{Diacritic}/gu, '')
            .toLowerCase()
            .trim();
    }
}
