import { Route } from '@angular/router';
import { ProductsComponent } from './products.component';

export const productsRoutes: Route[] = [
    {
        path: 'products',
        component: ProductsComponent,
    },
];
