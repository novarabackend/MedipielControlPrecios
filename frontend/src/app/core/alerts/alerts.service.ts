import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface AlertRule {
    id: number;
    brandId: number;
    brandName: string;
    listPriceThresholdPercent: number | null;
    promoPriceThresholdPercent: number | null;
    active: boolean;
}

export interface AlertRuleUpsert {
    listPriceThresholdPercent: number | null;
    promoPriceThresholdPercent: number | null;
    active: boolean;
}

export interface AlertItem {
    id: number;
    type: string;
    message: string;
    status: string;
    createdAt: string;
    productId: number;
    productSku: string | null;
    productEan: string | null;
    productDescription: string;
    brandName: string | null;
    competitorId: number;
    competitorName: string;
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class AlertsService {
    private _http = inject(HttpClient);

    getRules(): Observable<AlertRule[]> {
        return this._http.get<AlertRule[]>(`${API_BASE}/alerts/rules`);
    }

    upsertRule(brandId: number, payload: AlertRuleUpsert): Observable<AlertRule> {
        return this._http.put<AlertRule>(
            `${API_BASE}/alerts/rules/${brandId}`,
            payload
        );
    }

    getAlerts(): Observable<AlertItem[]> {
        return this._http.get<AlertItem[]>(`${API_BASE}/alerts`);
    }
}
