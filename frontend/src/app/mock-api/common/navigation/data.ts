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

const schedulerGroup: FuseNavigationItem = {
    id: 'scheduler',
    title: 'Scheduler',
    type: 'group',
    children: [
        {
            id: 'scheduler.settings',
            title: 'Configuracion',
            type: 'basic',
            icon: 'heroicons_outline:clock',
            link: '/settings/scheduler',
        },
        {
            id: 'scheduler.alerts',
            title: 'Alertas',
            type: 'basic',
            icon: 'heroicons_outline:bell-alert',
            link: '/settings/alerts',
        },
    ],
};

const resultsGroup: FuseNavigationItem = {
    id: 'results',
    title: 'Resultados',
    type: 'group',
    children: [
        {
            id: 'results.latest',
            title: 'Ultimo snapshot',
            type: 'basic',
            icon: 'heroicons_outline:chart-bar-square',
            link: '/results/latest',
        },
        {
            id: 'results.history',
            title: 'Historico',
            type: 'basic',
            icon: 'heroicons_outline:calendar-days',
            link: '/results/history',
        },
        {
            id: 'results.alerts',
            title: 'Alertas',
            type: 'basic',
            icon: 'heroicons_outline:bell-alert',
            link: '/results/alerts',
        },
    ],
};

export const defaultNavigation: FuseNavigationItem[] = [
    {
        id: 'dashboard',
        title: 'Dashboard',
        type: 'group',
        children: [
            {
                id: 'dashboard.home',
                title: 'Dashboard diario',
                type: 'basic',
                icon: 'heroicons_outline:chart-bar',
                link: '/dashboard',
            },
        ],
    },
    {
        id: 'catalog',
        title: 'Catalogo',
        type: 'group',
        children: [
            {
                id: 'catalog.products',
                title: 'Productos',
                type: 'basic',
                icon: 'heroicons_outline:archive-box',
                link: '/catalog/products',
            },
        ],
    },
    mastersGroup,
    schedulerGroup,
    resultsGroup,
];
export const compactNavigation: FuseNavigationItem[] = [
    {
        id: 'dashboard',
        title: 'Dashboard',
        type: 'group',
        children: [
            {
                id: 'dashboard.home',
                title: 'Dashboard diario',
                type: 'basic',
                icon: 'heroicons_outline:chart-bar',
                link: '/dashboard',
            },
        ],
    },
    {
        id: 'catalog',
        title: 'Catalogo',
        type: 'group',
        children: [
            {
                id: 'catalog.products',
                title: 'Productos',
                type: 'basic',
                icon: 'heroicons_outline:archive-box',
                link: '/catalog/products',
            },
        ],
    },
    mastersGroup,
    schedulerGroup,
    resultsGroup,
];
export const futuristicNavigation: FuseNavigationItem[] = [
    {
        id: 'dashboard',
        title: 'Dashboard',
        type: 'group',
        children: [
            {
                id: 'dashboard.home',
                title: 'Dashboard diario',
                type: 'basic',
                icon: 'heroicons_outline:chart-bar',
                link: '/dashboard',
            },
        ],
    },
    {
        id: 'catalog',
        title: 'Catalogo',
        type: 'group',
        children: [
            {
                id: 'catalog.products',
                title: 'Productos',
                type: 'basic',
                icon: 'heroicons_outline:archive-box',
                link: '/catalog/products',
            },
        ],
    },
    mastersGroup,
    schedulerGroup,
    resultsGroup,
];
export const horizontalNavigation: FuseNavigationItem[] = [
    {
        id: 'dashboard',
        title: 'Dashboard',
        type: 'group',
        children: [
            {
                id: 'dashboard.home',
                title: 'Dashboard diario',
                type: 'basic',
                icon: 'heroicons_outline:chart-bar',
                link: '/dashboard',
            },
        ],
    },
    {
        id: 'catalog',
        title: 'Catalogo',
        type: 'group',
        children: [
            {
                id: 'catalog.products',
                title: 'Productos',
                type: 'basic',
                icon: 'heroicons_outline:archive-box',
                link: '/catalog/products',
            },
        ],
    },
    mastersGroup,
    schedulerGroup,
    resultsGroup,
];
