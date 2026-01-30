/* eslint-disable */
import { FuseNavigationItem } from '@fuse/components/navigation';

const mastersGroup: FuseNavigationItem = {
    id      : 'masters',
    title   : 'Maestros de configuracion',
    type    : 'group',
    children: [
        {
            id   : 'masters.brands',
            title: 'Marcas',
            type : 'basic',
            icon : 'heroicons_outline:tag',
            link : '/masters/brands',
        },
        {
            id   : 'masters.suppliers',
            title: 'Proveedores',
            type : 'basic',
            icon : 'heroicons_outline:building-storefront',
            link : '/masters/suppliers',
        },
        {
            id   : 'masters.categories',
            title: 'Categorias',
            type : 'basic',
            icon : 'heroicons_outline:squares-2x2',
            link : '/masters/categories',
        },
        {
            id   : 'masters.lines',
            title: 'Lineas',
            type : 'basic',
            icon : 'heroicons_outline:rectangle-stack',
            link : '/masters/lines',
        },
    ],
};

export const defaultNavigation: FuseNavigationItem[] = [
    mastersGroup,
];
export const compactNavigation: FuseNavigationItem[] = [
    mastersGroup,
];
export const futuristicNavigation: FuseNavigationItem[] = [
    mastersGroup,
];
export const horizontalNavigation: FuseNavigationItem[] = [
    mastersGroup,
];
