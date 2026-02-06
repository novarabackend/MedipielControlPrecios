import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { catchError, finalize } from 'rxjs/operators';
import { forkJoin, of } from 'rxjs';
import * as XLSX from 'xlsx';
import { MasterItem, MastersService } from 'app/core/masters/masters.service';
import { AlertRule, AlertsService } from 'app/core/alerts/alerts.service';
import { ReportsService } from 'app/core/reports/reports.service';
import {
    CompetitorInfo as SnapshotCompetitor,
    LatestSnapshotResponse,
    SnapshotPrice,
    SnapshotRow,
    PriceSnapshotsService,
} from 'app/core/price-snapshots/price-snapshots.service';
import {
    ImportSummary,
    ProductImportItem,
    ProductItem,
    ProductsService,
} from 'app/core/products/products.service';

interface ParsedRows {
    items: ProductImportItem[];
    skippedEmpty: number;
}

interface ProductRowView {
    id: number;
    sku: string;
    ean: string | null;
    description: string;
    brandName: string | null;
    supplierName: string | null;
    categoryName: string | null;
    lineName: string | null;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
    pricesByCompetitor: Record<number, SnapshotPrice>;
}

@Component({
    selector: 'app-products',
    templateUrl: './products.component.html',
    styleUrls: ['./products.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [
        CommonModule,
        MatButtonModule,
        MatIconModule,
        MatTableModule,
        MatFormFieldModule,
        MatInputModule,
        MatSelectModule,
        RouterLink,
    ],
})
export class ProductsComponent {
    private _service = inject(ProductsService);
    private _mastersService = inject(MastersService);
    private _snapshotsService = inject(PriceSnapshotsService);
    private _alertsService = inject(AlertsService);
    private _reportsService = inject(ReportsService);

    readonly items = signal<ProductItem[]>([]);
    readonly loading = signal(false);
    readonly error = signal('');
    readonly snapshotData = signal<LatestSnapshotResponse | null>(null);
    readonly snapshotLoading = signal(false);
    readonly alertRules = signal<AlertRule[]>([]);
    readonly exportFrom = signal('');
    readonly exportTo = signal('');
    readonly exporting = signal(false);
    readonly exportError = signal('');

    readonly importing = signal(false);
    readonly importFile = signal<File | null>(null);
    readonly importSummary = signal<ImportSummary | null>(null);
    readonly showImportPanel = signal(false);
    readonly importRows = signal<Array<Array<string | number>>>([]);
    readonly skippedEmptyRows = signal(0);
    readonly showAllPreview = signal(false);

    readonly displayedColumns = [
        'sku',
        'ean',
        'brand',
        'supplier',
        'category',
        'line',
        'description',
    ];

    readonly brands = signal<MasterItem[]>([]);
    readonly suppliers = signal<MasterItem[]>([]);
    readonly categories = signal<MasterItem[]>([]);
    readonly lines = signal<MasterItem[]>([]);

    readonly filterEan = signal('');
    readonly filterName = signal('');
    readonly filterBrand = signal('');
    readonly filterSupplier = signal('');
    readonly filterCategory = signal('');
    readonly filterLine = signal('');
    readonly filterCompetitor = signal<number | null>(null);
    readonly filterStatus = signal('all');

    readonly hasError = computed(() => this.error().length > 0);
    readonly fileName = computed(() => this.importFile()?.name ?? 'Ningun archivo');
    readonly hasImportErrors = computed(
        () => (this.importSummary()?.errors?.length ?? 0) > 0
    );
    readonly headerRow = computed(() => this.importRows()[0] ?? []);
    readonly previewRows = computed(() => {
        const rows = this.importRows().slice(1);
        return this.showAllPreview() ? rows : rows.slice(0, 50);
    });
    readonly hasMorePreview = computed(() => {
        const total = Math.max(this.importRows().length - 1, 0);
        return !this.showAllPreview() && total > 50;
    });
    readonly errorMap = computed(() => {
        const map = new Map<number, string>();
        for (const errorItem of this.importSummary()?.errors ?? []) {
            const existing = map.get(errorItem.rowNumber);
            map.set(
                errorItem.rowNumber,
                existing ? `${existing} | ${errorItem.message}` : errorItem.message
            );
        }
        return map;
    });

    readonly competitors = computed<SnapshotCompetitor[]>(
        () => this.snapshotData()?.competitors ?? []
    );
    readonly snapshotDate = computed(() => this.snapshotData()?.snapshotDate ?? '—');

    readonly snapshotRowsByProductId = computed(() => {
        const map = new Map<number, SnapshotRow>();
        for (const row of this.snapshotData()?.rows ?? []) {
            map.set(row.productId, row);
        }
        return map;
    });

    readonly rowsView = computed<ProductRowView[]>(() => {
        const snapshots = this.snapshotRowsByProductId();
        return this.items().map((item) => {
            const snapshotRow = snapshots.get(item.id);
            const pricesByCompetitor: Record<number, SnapshotPrice> = {};
            for (const price of snapshotRow?.prices ?? []) {
                pricesByCompetitor[price.competitorId] = price;
            }

            return {
                id: item.id,
                sku: item.sku,
                ean: item.ean,
                description: item.description,
                brandName: item.brandName ?? null,
                supplierName: item.supplierName ?? null,
                categoryName: item.categoryName ?? null,
                lineName: item.lineName ?? null,
                medipielListPrice: snapshotRow?.medipielListPrice ?? item.medipielListPrice ?? null,
                medipielPromoPrice: snapshotRow?.medipielPromoPrice ?? item.medipielPromoPrice ?? null,
                pricesByCompetitor,
            };
        });
    });

    readonly filteredItems = computed(() => {
        const eanFilter = this.normalize(this.filterEan());
        const nameFilter = this.normalize(this.filterName());
        const brandFilter = this.normalize(this.filterBrand());
        const supplierFilter = this.normalize(this.filterSupplier());
        const categoryFilter = this.normalize(this.filterCategory());
        const lineFilter = this.normalize(this.filterLine());
        const competitorFilter = this.filterCompetitor();
        const statusFilter = this.filterStatus();

        return this.rowsView().filter((item) => {
            if (eanFilter && !this.normalize(item.ean ?? '').includes(eanFilter)) {
                return false;
            }

            if (nameFilter && !this.normalize(item.description ?? '').includes(nameFilter)) {
                return false;
            }

            if (brandFilter && !this.normalize(item.brandName ?? '').includes(brandFilter)) {
                return false;
            }

            if (supplierFilter && !this.normalize(item.supplierName ?? '').includes(supplierFilter)) {
                return false;
            }

            if (categoryFilter && !this.normalize(item.categoryName ?? '').includes(categoryFilter)) {
                return false;
            }

            if (lineFilter && !this.normalize(item.lineName ?? '').includes(lineFilter)) {
                return false;
            }

            if (statusFilter === 'no-ean') {
                return !item.ean;
            }

            if (statusFilter === 'matched') {
                return this.hasMatch(item, competitorFilter);
            }

            if (statusFilter === 'unmatched') {
                return !this.hasMatch(item, competitorFilter);
            }

            return true;
        });
    });

    readonly filteredCount = computed(() => this.filteredItems().length);
    readonly totalCount = computed(() => this.items().length);
    readonly noEanCount = computed(() => this.rowsView().filter((item) => !item.ean).length);
    readonly matchCount = computed(() => {
        const competitorId = this.filterCompetitor();
        return this.rowsView().filter((item) => this.hasMatch(item, competitorId)).length;
    });
    readonly matchPercent = computed(() => {
        const total = this.rowsView().length;
        return total > 0 ? (this.matchCount() / total) * 100 : 0;
    });
    readonly alertCount = computed(() => {
        const competitorId = this.filterCompetitor();
        return this.rowsView().filter((item) =>
            this.hasAlert(item, competitorId, this.getListThreshold(item.brandName))
        ).length;
    });
    readonly hoverCompetitors = computed(() => {
        const competitorId = this.filterCompetitor();
        const list = this.competitors();
        if (competitorId) {
            return list.filter((item) => item.id === competitorId);
        }
        return list.slice(0, 2);
    });

    constructor() {
        this.load();
        this.loadMasters();
        this.loadSnapshot();
        this.loadAlertRules();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');
        this._service
            .getProducts()
            .pipe(finalize(() => this.loading.set(false)))
            .subscribe({
                next: (items) => this.items.set(items),
                error: () => this.error.set('No se pudo cargar productos.'),
            });
    }

    loadSnapshot(): void {
        this.snapshotLoading.set(true);
        this._snapshotsService
            .getLatest(500)
            .pipe(finalize(() => this.snapshotLoading.set(false)))
            .subscribe({
                next: (data) => this.snapshotData.set(data),
                error: () => {
                    this.snapshotData.set(null);
                },
            });
    }

    loadAlertRules(): void {
        this._alertsService.getRules().subscribe({
            next: (rules) => this.alertRules.set(rules),
            error: () => this.alertRules.set([]),
        });
    }

    loadMasters(): void {
        forkJoin({
            brands: this._mastersService
                .getBrands()
                .pipe(catchError(() => of([]))),
            suppliers: this._mastersService
                .getSuppliers()
                .pipe(catchError(() => of([]))),
            categories: this._mastersService
                .getCategories()
                .pipe(catchError(() => of([]))),
            lines: this._mastersService
                .getLines()
                .pipe(catchError(() => of([]))),
        }).subscribe((result) => {
            this.brands.set(result.brands);
            this.suppliers.set(result.suppliers);
            this.categories.set(result.categories);
            this.lines.set(result.lines);
        });
    }

    toggleImportPanel(): void {
        this.showImportPanel.set(!this.showImportPanel());
    }

    resetFilters(): void {
        this.filterEan.set('');
        this.filterName.set('');
        this.filterBrand.set('');
        this.filterSupplier.set('');
        this.filterCategory.set('');
        this.filterLine.set('');
        this.filterCompetitor.set(null);
        this.filterStatus.set('all');
    }

    exportExcel(): void {
        const from = this.exportFrom() || this.snapshotDate();
        const to = this.exportTo() || from;
        if (!from || from === '—') {
            this.exportError.set('Selecciona una fecha valida para exportar.');
            return;
        }

        const ids = this.filteredItems().map((item) => item.id);
        if (ids.length === 0) {
            this.exportError.set('No hay productos para exportar.');
            return;
        }

        this.exporting.set(true);
        this.exportError.set('');

        this._reportsService
            .downloadExcel({
                from,
                to,
                brandId: this.filterBrand()
                    ? Number(this.filterBrand())
                    : null,
                categoryId: this.filterCategory()
                    ? Number(this.filterCategory())
                    : null,
                productIds: ids,
            })
            .pipe(finalize(() => this.exporting.set(false)))
            .subscribe({
                next: (blob) => {
                    const url = window.URL.createObjectURL(blob);
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `productos_${from}_${to}.xlsx`;
                    link.click();
                    window.setTimeout(() => window.URL.revokeObjectURL(url), 0);
                },
                error: () => {
                    this.exportError.set('No se pudo exportar el Excel.');
                },
            });
    }

    hasMatch(item: ProductRowView, competitorId: number | null): boolean {
        if (!competitorId) {
            return Object.values(item.pricesByCompetitor).some(
                (price) =>
                    price.listPrice !== null ||
                    price.promoPrice !== null ||
                    !!price.url
            );
        }

        const price = item.pricesByCompetitor[competitorId];
        if (!price) {
            return false;
        }
        return price.listPrice !== null || price.promoPrice !== null || !!price.url;
    }

    hasAlert(
        item: ProductRowView,
        competitorId: number | null,
        thresholdPercent: number | null
    ): boolean {
        if (thresholdPercent === null) {
            return false;
        }

        const basePrice = item.medipielListPrice ?? null;
        if (!basePrice) {
            return false;
        }

        const evaluatePrice = (price: SnapshotPrice | undefined): boolean => {
            if (!price || price.listPrice === null || price.listPrice === undefined) {
                return false;
            }
            const percent = ((price.listPrice - basePrice) / basePrice) * 100;
            return Math.abs(percent) >= thresholdPercent;
        };

        if (competitorId) {
            return evaluatePrice(item.pricesByCompetitor[competitorId]);
        }

        return Object.values(item.pricesByCompetitor).some((price) =>
            evaluatePrice(price)
        );
    }

    private getListThreshold(brandName?: string | null): number | null {
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

        return rule.listPriceThresholdPercent ?? defaultThreshold;
    }

    getCompetitorLabel(): string {
        const competitorId = this.filterCompetitor();
        if (!competitorId) {
            return 'Todos';
        }
        return (
            this.competitors().find((item) => item.id === competitorId)?.name ??
            'Todos'
        );
    }

    onFileChange(event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0] ?? null;
        this.importFile.set(file);
        this.importSummary.set(null);
        this.importRows.set([]);
        this.showAllPreview.set(false);
    }

    downloadTemplate(): void {
        const worksheet = XLSX.utils.aoa_to_sheet([
            [
                'SKU',
                'EAN',
                'Descripción',
                'Marca',
                'Proveedor',
                'Categoria',
                'Linea',
                'ABC',
                'Precio Descuento',
                'Precio Normal',
            ],
        ]);
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, worksheet, 'Productos');
        XLSX.writeFile(workbook, 'plantilla_productos.xlsx');
    }

    importExcel(): void {
        const file = this.importFile();
        if (!file) {
            this.error.set('Selecciona un archivo de Excel.');
            return;
        }

        this.importing.set(true);
        this.error.set('');
        this.importSummary.set(null);
        this.skippedEmptyRows.set(0);
        this.showAllPreview.set(false);

        const reader = new FileReader();
        reader.onload = () => {
            try {
                const data = new Uint8Array(reader.result as ArrayBuffer);
                const workbook = XLSX.read(data, { type: 'array' });
                const sheetName = workbook.SheetNames[0];
                const sheet = workbook.Sheets[sheetName];
                const rows = XLSX.utils.sheet_to_json<Array<string | number>>(sheet, {
                    header: 1,
                    blankrows: false,
                });
                this.importRows.set(rows);

                const parsed = this.parseRows(rows);
                if (parsed.items.length === 0) {
                    this.error.set('El archivo no tiene filas validas.');
                    this.importing.set(false);
                    return;
                }
                this.skippedEmptyRows.set(parsed.skippedEmpty);

                this._service
                    .importProducts(parsed.items)
                    .pipe(finalize(() => this.importing.set(false)))
                    .subscribe({
                        next: (summary) => {
                            this.importSummary.set(summary);
                            this.load();
                        },
                        error: () => {
                            this.error.set('No se pudo importar el archivo.');
                        },
                    });
            } catch {
                this.error.set('No se pudo leer el archivo.');
                this.importing.set(false);
            }
        };
        reader.readAsArrayBuffer(file);
    }

    downloadErrors(): void {
        const rows = this.importRows();
        if (rows.length === 0 || !this.hasImportErrors()) {
            return;
        }

        const errorMap = new Map<number, string>();
        for (const errorItem of this.importSummary()?.errors ?? []) {
            const existing = errorMap.get(errorItem.rowNumber);
            const message = errorItem.message;
            errorMap.set(
                errorItem.rowNumber,
                existing ? `${existing} | ${message}` : message
            );
        }

        const outputRows = rows.map((row, index) => {
            if (index === 0) {
                return [...row, 'Error'];
            }
            const excelRow = index + 1;
            const message = errorMap.get(excelRow) ?? '';
            return [...row, message];
        });

        const worksheet = XLSX.utils.aoa_to_sheet(outputRows);
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, worksheet, 'Errores');

        const originalName = this.importFile()?.name ?? 'productos.xlsx';
        const baseName = originalName.replace(/\\.xlsx?$/i, '');
        XLSX.writeFile(workbook, `${baseName}-errores.xlsx`);
    }

    showAllRows(): void {
        this.showAllPreview.set(true);
    }

    private parseRows(rows: Array<Array<string | number>>): ParsedRows {
        if (rows.length === 0) {
            return { items: [], skippedEmpty: 0 };
        }

        const header = rows[0].map((cell) => this.normalize(String(cell ?? '')));
        const indexOf = (aliases: string[]) => {
            for (const alias of aliases) {
                const idx = header.indexOf(this.normalize(alias));
                if (idx >= 0) {
                    return idx;
                }
            }
            return -1;
        };

        const skuIndex = indexOf(['sku', 'ref']);
        const eanIndex = indexOf([
            'ean',
            'codigo barra principal',
            'codigo barra',
            'codigo barras',
            'codigo de barras',
            'codigo barra principal',
        ]);
        const descriptionIndex = indexOf(['descripcion', 'descripción']);
        const brandIndex = indexOf(['marca', 'marcas']);
        const supplierIndex = indexOf(['proveedor']);
        const categoryIndex = indexOf(['categoria', 'categoría']);
        const lineIndex = indexOf(['linea', 'línea']);
        const listPriceIndex = indexOf(['precio normal', 'precio lista', 'precio de lista']);
        const promoPriceIndex = indexOf([
            'precio descuento',
            'precio promo',
            'precio promocion',
            'precio promoción',
        ]);

        if (eanIndex < 0 || descriptionIndex < 0) {
            return { items: [], skippedEmpty: 0 };
        }

        const items: ProductImportItem[] = [];
        let skippedEmpty = 0;

        for (let i = 1; i < rows.length; i += 1) {
            const row = rows[i] ?? [];
            const sku = this.getCell(row, skuIndex);
            const ean = this.getCell(row, eanIndex);
            const description = this.getCell(row, descriptionIndex);
            const brand = this.getCell(row, brandIndex);
            const supplier = this.getCell(row, supplierIndex);
            const category = this.getCell(row, categoryIndex);
            const line = this.getCell(row, lineIndex);
            const medipielListPrice = this.getNumberCell(row, listPriceIndex);
            const medipielPromoPrice = this.getNumberCell(row, promoPriceIndex);

            const hasAnyValue = Boolean(
                sku ||
                    ean ||
                    description ||
                    brand ||
                    supplier ||
                    category ||
                    line ||
                    medipielListPrice ||
                    medipielPromoPrice
            );
            if (!hasAnyValue) {
                skippedEmpty += 1;
                continue;
            }

            items.push({
                rowNumber: i + 1,
                sku,
                ean,
                description,
                brand,
                supplier,
                category,
                line,
                medipielListPrice,
                medipielPromoPrice,
            });
        }

        return { items, skippedEmpty };
    }

    private getCell(row: Array<string | number>, index: number): string | null {
        if (index < 0) {
            return null;
        }
        const value = row[index];
        if (value === null || value === undefined) {
            return null;
        }
        const text = String(value).trim();
        return text.length > 0 ? text : null;
    }

    private getNumberCell(row: Array<string | number>, index: number): number | null {
        if (index < 0) {
            return null;
        }
        const value = row[index];
        if (value === null || value === undefined) {
            return null;
        }
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }
        const digits = String(value).replace(/[^0-9]/g, '');
        if (!digits) {
            return null;
        }
        const parsed = Number(digits);
        return Number.isFinite(parsed) ? parsed : null;
    }

    private normalize(value: string): string {
        return value
            .normalize('NFD')
            .replace(/\p{Diacritic}/gu, '')
            .toLowerCase()
            .trim();
    }
}
