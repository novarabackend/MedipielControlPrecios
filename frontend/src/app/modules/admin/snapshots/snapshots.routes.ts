import { Route } from '@angular/router';
import { SnapshotsComponent } from './snapshots.component';

export const snapshotsRoutes: Route[] = [
    {
        path: 'latest',
        component: SnapshotsComponent,
    },
];
