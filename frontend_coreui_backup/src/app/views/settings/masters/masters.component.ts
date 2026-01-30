import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ColComponent,
  FormControlDirective,
  FormDirective,
  InputGroupComponent,
  InputGroupTextDirective,
  RowComponent
} from '@coreui/angular';
import { forkJoin } from 'rxjs';
import { MastersService, MasterItem } from '../../../services/masters.service';

@Component({
  selector: 'app-masters',
  templateUrl: './masters.component.html',
  styleUrls: ['./masters.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RowComponent,
    ColComponent,
    CardComponent,
    CardHeaderComponent,
    CardBodyComponent,
    FormDirective,
    FormControlDirective,
    InputGroupComponent,
    InputGroupTextDirective,
    ButtonDirective,
    ReactiveFormsModule
  ]
})
export class MastersComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(MastersService);

  readonly brands = signal<MasterItem[]>([]);
  readonly suppliers = signal<MasterItem[]>([]);
  readonly categories = signal<MasterItem[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');

  readonly editing = signal<{ type: 'brand' | 'supplier' | 'category'; id: number } | null>(null);

  readonly brandForm = this.fb.group({
    name: ['', [Validators.required]]
  });

  readonly supplierForm = this.fb.group({
    name: ['', [Validators.required]]
  });

  readonly categoryForm = this.fb.group({
    name: ['', [Validators.required]]
  });

  readonly editForm = this.fb.group({
    name: ['', [Validators.required]]
  });

  readonly hasError = computed(() => this.error().length > 0);

  constructor() {
    this.loadAll();
  }

  loadAll(): void {
    this.loading.set(true);
    this.error.set('');

    forkJoin({
      brands: this.service.getBrands(),
      suppliers: this.service.getSuppliers(),
      categories: this.service.getCategories()
    }).subscribe({
      next: ({ brands, suppliers, categories }) => {
        this.brands.set(brands);
        this.suppliers.set(suppliers);
        this.categories.set(categories);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('No se pudo cargar los maestros.');
        this.loading.set(false);
      }
    });
  }

  startEdit(type: 'brand' | 'supplier' | 'category', item: MasterItem): void {
    this.editing.set({ type, id: item.id });
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
    const payload = this.editForm.value.name?.trim() ?? '';
    const target = this.editing();
    if (!target) {
      return;
    }

    const done = () => this.cancelEdit();

    if (target.type === 'brand') {
      this.service.updateBrand(target.id, payload).subscribe({
        next: () => this.loadBrands(done),
        error: () => this.error.set('No se pudo actualizar Marca.')
      });
    }

    if (target.type === 'supplier') {
      this.service.updateSupplier(target.id, payload).subscribe({
        next: () => this.loadSuppliers(done),
        error: () => this.error.set('No se pudo actualizar Proveedor.')
      });
    }

    if (target.type === 'category') {
      this.service.updateCategory(target.id, payload).subscribe({
        next: () => this.loadCategories(done),
        error: () => this.error.set('No se pudo actualizar Categoria.')
      });
    }
  }

  createBrand(): void {
    if (this.brandForm.invalid) {
      this.brandForm.markAllAsTouched();
      return;
    }
    const name = this.brandForm.value.name?.trim() ?? '';
    this.service.createBrand(name).subscribe({
      next: () => this.loadBrands(() => this.brandForm.reset()),
      error: () => this.error.set('No se pudo crear Marca.')
    });
  }

  createSupplier(): void {
    if (this.supplierForm.invalid) {
      this.supplierForm.markAllAsTouched();
      return;
    }
    const name = this.supplierForm.value.name?.trim() ?? '';
    this.service.createSupplier(name).subscribe({
      next: () => this.loadSuppliers(() => this.supplierForm.reset()),
      error: () => this.error.set('No se pudo crear Proveedor.')
    });
  }

  createCategory(): void {
    if (this.categoryForm.invalid) {
      this.categoryForm.markAllAsTouched();
      return;
    }
    const name = this.categoryForm.value.name?.trim() ?? '';
    this.service.createCategory(name).subscribe({
      next: () => this.loadCategories(() => this.categoryForm.reset()),
      error: () => this.error.set('No se pudo crear Categoria.')
    });
  }

  deleteBrand(item: MasterItem): void {
    this.service.deleteBrand(item.id).subscribe({
      next: () => this.loadBrands(),
      error: () => this.error.set('No se pudo eliminar Marca.')
    });
  }

  deleteSupplier(item: MasterItem): void {
    this.service.deleteSupplier(item.id).subscribe({
      next: () => this.loadSuppliers(),
      error: () => this.error.set('No se pudo eliminar Proveedor.')
    });
  }

  deleteCategory(item: MasterItem): void {
    this.service.deleteCategory(item.id).subscribe({
      next: () => this.loadCategories(),
      error: () => this.error.set('No se pudo eliminar Categoria.')
    });
  }

  private loadBrands(after?: () => void): void {
    this.service.getBrands().subscribe({
      next: (items) => {
        this.brands.set(items);
        after?.();
      },
      error: () => this.error.set('No se pudo cargar Marcas.')
    });
  }

  private loadSuppliers(after?: () => void): void {
    this.service.getSuppliers().subscribe({
      next: (items) => {
        this.suppliers.set(items);
        after?.();
      },
      error: () => this.error.set('No se pudo cargar Proveedores.')
    });
  }

  private loadCategories(after?: () => void): void {
    this.service.getCategories().subscribe({
      next: (items) => {
        this.categories.set(items);
        after?.();
      },
      error: () => this.error.set('No se pudo cargar Categorias.')
    });
  }
}
