import { Route } from '@angular/router';

export const mastersRoutes: Route[] = [
    {
        path: '',
        pathMatch: 'full',
        redirectTo: 'brands',
    },
    {
        path: 'brands',
        loadComponent: () =>
            import('app/modules/admin/masters/brands/brands.component').then(
                (m) => m.BrandsComponent
            ),
    },
    {
        path: 'suppliers',
        loadComponent: () =>
            import(
                'app/modules/admin/masters/suppliers/suppliers.component'
            ).then((m) => m.SuppliersComponent),
    },
    {
        path: 'categories',
        loadComponent: () =>
            import(
                'app/modules/admin/masters/categories/categories.component'
            ).then((m) => m.CategoriesComponent),
    },
    {
        path: 'lines',
        loadComponent: () =>
            import('app/modules/admin/masters/lines/lines.component').then(
                (m) => m.LinesComponent
            ),
    },
];
