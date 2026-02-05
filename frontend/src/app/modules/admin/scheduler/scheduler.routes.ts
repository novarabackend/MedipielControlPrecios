import { Route } from '@angular/router';
import { SchedulerComponent } from './scheduler.component';
import { AlertsSettingsComponent } from './alerts.component';

export const schedulerRoutes: Route[] = [
    {
        path: 'scheduler',
        component: SchedulerComponent,
    },
    {
        path: 'alerts',
        component: AlertsSettingsComponent,
    },
];
