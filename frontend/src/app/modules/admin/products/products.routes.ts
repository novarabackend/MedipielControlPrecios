import { Route } from '@angular/router';
import { ProductsComponent } from './products.component';
import { ProductDetailComponent } from './product-detail/product-detail.component';

export const productsRoutes: Route[] = [
    {
        path: 'products/:id',
        component: ProductDetailComponent,
    },
    {
        path: 'products',
        component: ProductsComponent,
    },
];
