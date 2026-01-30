import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, concatMap, finalize } from 'rxjs/operators';
import { from, of } from 'rxjs';
import * as XLSX from 'xlsx';
import { MastersService, MasterItem } from 'app/core/masters/masters.service';

interface ImportSummary {
    total: number;
    success: number;
    failed: number;
    skipped: number;
}

@Component({
    selector: 'app-suppliers',
    templateUrl: './suppliers.component.html',
    styleUrls: ['./suppliers.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
    imports: [ReactiveFormsModule, MatButtonModule, MatIconModule],
})
export class SuppliersComponent {
    private _fb = inject(FormBuilder);
    private _service = inject(MastersService);

    readonly items = signal<MasterItem[]>([]);
    readonly loading = signal(false);
    readonly error = signal('');

    readonly importing = signal(false);
    readonly importFile = signal<File | null>(null);
    readonly importSummary = signal<ImportSummary | null>(null);
    readonly showCreatePanel = signal(false);
    readonly showImportPanel = signal(false);

    readonly editing = signal<MasterItem | null>(null);

    readonly createForm = this._fb.group({
        name: ['', [Validators.required]],
    });

    readonly editForm = this._fb.group({
        name: ['', [Validators.required]],
    });

    readonly hasError = computed(() => this.error().length > 0);
    readonly fileName = computed(() => this.importFile()?.name ?? 'Ningun archivo');

    constructor() {
        this.load();
    }

    load(): void {
        this.loading.set(true);
        this.error.set('');
        this._service.getSuppliers().subscribe({
            next: (items) => {
                this.items.set(items);
                this.loading.set(false);
            },
            error: () => {
                this.error.set('No se pudo cargar Proveedores.');
                this.loading.set(false);
            },
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
        this._service.createSupplier(name).subscribe({
            next: () => {
                this.createForm.reset();
                this.load();
                this.showCreatePanel.set(false);
            },
            error: () => this.error.set('No se pudo crear el proveedor.'),
        });
    }

    startEdit(item: MasterItem): void {
        this.editing.set(item);
        this.editForm.reset({ name: item.name });
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
        const target = this.editing();
        if (!target) {
            return;
        }

        this._service.updateSupplier(target.id, name).subscribe({
            next: () => {
                this.cancelEdit();
                this.load();
            },
            error: () => this.error.set('No se pudo actualizar el proveedor.'),
        });
    }

    remove(item: MasterItem): void {
        this._service.deleteSupplier(item.id).subscribe({
            next: () => this.load(),
            error: () => this.error.set('No se pudo eliminar el proveedor.'),
        });
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
                const names = rows
                    .map((row) => String(row?.[0] ?? '').trim())
                    .filter((value) => value.length > 0);

                if (names.length === 0) {
                    this.error.set('El archivo no tiene datos.');
                    this.importing.set(false);
                    return;
                }

                if (names[0].toLowerCase().includes('nombre')) {
                    names.shift();
                }

                const uniqueNames = Array.from(new Set(names));
                const summary: ImportSummary = {
                    total: uniqueNames.length,
                    success: 0,
                    failed: 0,
                    skipped: 0,
                };

                from(uniqueNames)
                    .pipe(
                        concatMap((name) =>
                            this._service.createSupplier(name).pipe(
                                catchError((error) => {
                                    if (error?.status === 409) {
                                        summary.skipped += 1;
                                    } else {
                                        summary.failed += 1;
                                    }
                                    return of(null);
                                })
                            )
                        ),
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
        const data = [['Nombre']];
        const worksheet = XLSX.utils.aoa_to_sheet(data);
        const workbook = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(workbook, worksheet, 'Proveedores');

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
        anchor.download = 'plantilla_proveedores.xlsx';
        anchor.click();
        window.URL.revokeObjectURL(url);
    }
}
