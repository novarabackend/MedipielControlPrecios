import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'brands',
    pathMatch: 'full'
  },
  {
    path: 'brands',
    loadComponent: () => import('./brands/brands.component').then(m => m.BrandsComponent),
    data: {
      title: $localize`Marcas`
    }
  },
  {
    path: 'suppliers',
    loadComponent: () => import('./suppliers/suppliers.component').then(m => m.SuppliersComponent),
    data: {
      title: $localize`Proveedores`
    }
  },
  {
    path: 'categories',
    loadComponent: () => import('./categories/categories.component').then(m => m.CategoriesComponent),
    data: {
      title: $localize`Categorias`
    }
  }
];
