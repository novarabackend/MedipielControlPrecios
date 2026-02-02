import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { catchError, finalize } from 'rxjs/operators';
import { of } from 'rxjs';
import * as XLSX from 'xlsx';
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

@Component({
    selector: 'app-products',
    templateUrl: './products.component.html',
    styleUrls: ['./products.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [MatButtonModule, MatIconModule, MatTableModule],
})
export class ProductsComponent {
    private _service = inject(ProductsService);

    readonly items = signal<ProductItem[]>([]);
    readonly loading = signal(false);
    readonly error = signal('');

    readonly importing = signal(false);
    readonly importFile = signal<File | null>(null);
    readonly importSummary = signal<ImportSummary | null>(null);
    readonly showImportPanel = signal(false);
    readonly importRows = signal<Array<Array<string | number>>>([]);
    readonly skippedEmptyRows = signal(0);
    readonly showAllPreview = signal(false);

    readonly displayedColumns = ['sku', 'ean', 'description'];

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

    constructor() {
        this.load();
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

    toggleImportPanel(): void {
        this.showImportPanel.set(!this.showImportPanel());
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
                'ABC',
                'numeración',
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
