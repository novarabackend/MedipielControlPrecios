import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, concatMap, finalize, map, switchMap, tap } from 'rxjs/operators';
import { from, of } from 'rxjs';
import * as XLSX from 'xlsx';
import { MastersService, MasterItem } from 'app/core/masters/masters.service';
import { AlertRule, AlertsService } from 'app/core/alerts/alerts.service';

interface ImportSummary {
    total: number;
    success: number;
    failed: number;
    skipped: number;
}

@Component({
    selector: 'app-brands',
    templateUrl: './brands.component.html',
    styleUrls: ['./brands.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [ReactiveFormsModule, MatButtonModule, MatIconModule],
})
export class BrandsComponent {
    private _fb = inject(FormBuilder);
    private _service = inject(MastersService);
    private _alertsService = inject(AlertsService);

    readonly items = signal<MasterItem[]>([]);
    readonly loading = signal(false);
    readonly error = signal('');
    readonly suppliers = signal<MasterItem[]>([]);
    readonly alertRules = signal<AlertRule[]>([]);

    readonly importing = signal(false);
    readonly importFile = signal<File | null>(null);
    readonly importSummary = signal<ImportSummary | null>(null);
    readonly showCreatePanel = signal(false);
    readonly showImportPanel = signal(false);

    readonly editing = signal<MasterItem | null>(null);

    readonly createForm = this._fb.group({
        name: ['', [Validators.required]],
        supplier: ['', [Validators.required]],
        alertPercent: this._fb.control<number | null>(null, [Validators.min(0)]),
    });

    readonly editForm = this._fb.group({
        name: ['', [Validators.required]],
        supplier: ['', [Validators.required]],
        alertPercent: this._fb.control<number | null>(null, [Validators.min(0)]),
    });

    readonly hasError = computed(() => this.error().length > 0);
    readonly fileName = computed(() => this.importFile()?.name ?? 'Ningun archivo');
    readonly supplierMap = computed(() => {
        const map = new Map<string, number>();
        for (const supplier of this.suppliers()) {
            map.set(supplier.name.toLowerCase(), supplier.id);
        }
        return map;
    });
    readonly alertRuleMap = computed(() => {
        const map = new Map<number, AlertRule>();
        for (const rule of this.alertRules()) {
            map.set(rule.brandId, rule);
        }
        return map;
    });

    constructor() {
        this.load();
        this.loadSuppliers();
        this.loadAlertRules();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');
        this._service.getBrands().subscribe({
            next: (items) => {
                this.items.set(items);
                this.loading.set(false);
            },
            error: () => {
                this.error.set('No se pudo cargar Marcas.');
                this.loading.set(false);
            },
        });
    }

    loadSuppliers(): void {
        this._service.getSuppliers().subscribe({
            next: (items) => this.suppliers.set(items),
            error: () => this.error.set('No se pudo cargar Proveedores.'),
        });
    }

    loadAlertRules(): void {
        this._alertsService.getRules().subscribe({
            next: (items) => this.alertRules.set(items),
            error: () => this.alertRules.set([]),
        });
    }

    toggleCreatePanel(): void {
        const next = !this.showCreatePanel();
        this.showCreatePanel.set(next);
        if (next) {
            this.showImportPanel.set(false);
        }
    }

    toggleImportPanel(): void {
        const next = !this.showImportPanel();
        this.showImportPanel.set(next);
        if (next) {
            this.showCreatePanel.set(false);
        }
    }

    create(): void {
        if (this.createForm.invalid) {
            this.createForm.markAllAsTouched();
            return;
        }
        const name = this.createForm.value.name?.trim() ?? '';
        const supplierName = this.createForm.value.supplier?.trim() ?? '';
        if (!supplierName) {
            this.error.set('El proveedor es obligatorio.');
            return;
        }

        this.resolveSupplierId(supplierName).subscribe({
            next: (supplierId) => {
                if (!supplierId) {
                    this.error.set('No se pudo resolver el proveedor.');
                    return;
                }
                const threshold = this.toNumberOrNull(
                    this.createForm.value.alertPercent
                );
                const payload = this.buildAlertPayload(threshold);

                this._service
                    .createBrand(name, supplierId)
                    .pipe(
                        switchMap((created) =>
                            this._alertsService
                                .upsertRule(created.id, payload)
                                .pipe(
                                    catchError(() => {
                                        this.error.set(
                                            'La marca se creo, pero no se pudo guardar la regla de alertas.'
                                        );
                                        return of(null);
                                    })
                                )
                        )
                    )
                    .subscribe({
                        next: () => {
                            this.createForm.reset({
                                name: '',
                                supplier: '',
                                alertPercent: null,
                            });
                            this.load();
                            this.loadAlertRules();
                            this.showCreatePanel.set(false);
                        },
                        error: () => this.error.set('No se pudo crear la marca.'),
                    });
            },
            error: () => this.error.set('No se pudo resolver el proveedor.'),
        });
    }

    startEdit(item: MasterItem): void {
        this.editing.set(item);
        const supplierName = this.supplierNameFor(item.supplierId);
        const rule = this.alertRuleMap().get(item.id);
        const threshold = rule?.active
            ? (rule.listPriceThresholdPercent ?? rule.promoPriceThresholdPercent ?? null)
            : null;
        this.editForm.reset({
            name: item.name,
            supplier: supplierName === '—' ? '' : supplierName,
            alertPercent: threshold,
        });
    }

    cancelEdit(): void {
        this.editing.set(null);
        this.editForm.reset();
    }

    saveEdit(): void {
        if (this.editForm.invalid || !this.editing()) {
            this.editForm.markAllAsTouched();
            return;
        }

        const name = this.editForm.value.name?.trim() ?? '';
        const supplierName = this.editForm.value.supplier?.trim() ?? '';
        const target = this.editing();
        if (!target) {
            return;
        }

        if (!supplierName) {
            this.error.set('El proveedor es obligatorio.');
            return;
        }

        this.resolveSupplierId(supplierName).subscribe({
            next: (supplierId) => {
                if (!supplierId) {
                    this.error.set('No se pudo resolver el proveedor.');
                    return;
                }
                const threshold = this.toNumberOrNull(
                    this.editForm.value.alertPercent
                );
                const payload = this.buildAlertPayload(threshold);

                this._service
                    .updateBrand(target.id, name, supplierId)
                    .pipe(
                        switchMap(() =>
                            this._alertsService.upsertRule(target.id, payload).pipe(
                                catchError(() => {
                                    this.error.set(
                                        'La marca se guardo, pero no se pudo guardar la regla de alertas.'
                                    );
                                    return of(null);
                                })
                            )
                        )
                    )
                    .subscribe({
                        next: () => {
                            this.cancelEdit();
                            this.load();
                            this.loadAlertRules();
                        },
                        error: () => this.error.set('No se pudo actualizar la marca.'),
                    });
            },
            error: () => this.error.set('No se pudo resolver el proveedor.'),
        });
    }

    remove(item: MasterItem): void {
        this._service.deleteBrand(item.id).subscribe({
            next: () => this.load(),
            error: () => this.error.set('No se pudo eliminar la marca.'),
        });
    }

    getAlertThreshold(brandId: number): number | null {
        const rule = this.alertRuleMap().get(brandId);
        if (!rule || !rule.active) {
            return null;
        }
        return rule.listPriceThresholdPercent ?? rule.promoPriceThresholdPercent ?? null;
    }

    getAlertThresholdText(brandId: number): string {
        const threshold = this.getAlertThreshold(brandId);
        if (threshold === null || threshold === undefined) {
            return '—';
        }
        return `${threshold}%`;
    }

    private buildAlertPayload(threshold: number | null) {
        if (threshold === null) {
            return {
                listPriceThresholdPercent: null,
                promoPriceThresholdPercent: null,
                active: false,
            };
        }

        return {
            listPriceThresholdPercent: threshold,
            promoPriceThresholdPercent: threshold,
            active: true,
        };
    }

    private toNumberOrNull(value: unknown): number | null {
        if (value === null || value === undefined) {
            return null;
        }
        if (typeof value === 'number') {
            return Number.isFinite(value) ? value : null;
        }
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : null;
    }

    onFileChange(event: Event): void {
        const input = event.target as HTMLInputElement;
        const file = input.files?.[0] ?? null;
        this.importFile.set(file);
        this.importSummary.set(null);
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

                if (rows.length === 0) {
                    this.error.set('El archivo no tiene datos.');
                    this.importing.set(false);
                    return;
                }

                const header = rows[0] ?? [];
                const headerName = String(header?.[0] ?? '').toLowerCase();
                const headerSupplier = String(header?.[1] ?? '').toLowerCase();
                if (headerName.includes('nombre') && headerSupplier.includes('proveedor')) {
                    rows.shift();
                }

                const normalizedRows = rows
                    .map((row) => ({
                        name: String(row?.[0] ?? '').trim(),
                        supplier: String(row?.[1] ?? '').trim(),
                    }))
                    .filter((row) => row.name.length > 0 || row.supplier.length > 0);

                if (normalizedRows.length === 0) {
                    this.error.set('El archivo no tiene datos.');
                    this.importing.set(false);
                    return;
                }

                const summary: ImportSummary = {
                    total: normalizedRows.length,
                    success: 0,
                    failed: 0,
                    skipped: 0,
                };

                from(normalizedRows)
                    .pipe(
                        concatMap((row) => {
                            if (!row.name || !row.supplier) {
                                summary.failed += 1;
                                return of(null);
                            }
                            return this.resolveSupplierId(row.supplier).pipe(
                                switchMap((supplierId) => {
                                    if (!supplierId) {
                                        summary.failed += 1;
                                        return of(null);
                                    }
                                    return this._service.createBrand(row.name, supplierId).pipe(
                                        catchError((error) => {
                                            if (error?.status === 409) {
                                                summary.skipped += 1;
                                            } else {
                                                summary.failed += 1;
                                            }
                                            return of(null);
                                        })
                                    );
                                })
                            );
                        }),
                        finalize(() => {
                            this.importSummary.set(summary);
                            this.importing.set(false);
                            this.load();
                            this.showImportPanel.set(false);
                        })
                    )
                    .subscribe({
                        next: (result) => {
                            if (result) {
                                summary.success += 1;
                            }
                        },
                        error: () => {
                            this.error.set('No se pudo procesar el archivo.');
                            this.importing.set(false);
                        },
                    });
            } catch {
                this.error.set('El archivo no se pudo leer.');
                this.importing.set(false);
            }
        };
        reader.onerror = () => {
            this.error.set('El archivo no se pudo leer.');
            this.importing.set(false);
        };
        reader.readAsArrayBuffer(file);
    }

    downloadTemplate(): void {
        const data = [['Nombre', 'Proveedor']];
        const worksheet = XLSX.utils.aoa_to_sheet(data);
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, worksheet, 'Marcas');

        const excelBuffer = XLSX.write(workbook, {
            bookType: 'xlsx',
            type: 'array',
        });

        const blob = new Blob([excelBuffer], {
            type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        });
        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'plantilla_marcas.xlsx';
        anchor.click();
        window.URL.revokeObjectURL(url);
    }

    supplierNameFor(supplierId?: number | null): string {
        if (!supplierId) {
            return '—';
        }
        const supplier = this.suppliers().find((item) => item.id === supplierId);
        return supplier?.name ?? '—';
    }

    private resolveSupplierId(name: string) {
        const normalized = name.trim().toLowerCase();
        const existingId = this.supplierMap().get(normalized);
        if (existingId) {
            return of(existingId);
        }
        return this._service.createSupplier(name).pipe(
            tap((supplier) => {
                this.suppliers.update((items) => [...items, supplier]);
            }),
            map((supplier) => supplier.id),
            catchError((error) => {
                if (error?.status === 409) {
                    return this._service.getSuppliers().pipe(
                        tap((items) => this.suppliers.set(items)),
                        map((items) => {
                            const match = items.find(
                                (item) => item.name.toLowerCase() === normalized
                            );
                            return match?.id ?? null;
                        })
                    );
                }
                return of(null);
            })
        );
    }
}
