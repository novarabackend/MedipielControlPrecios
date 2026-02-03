import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface SnapshotPrice {
    competitorId: number;
    listPrice: number | null;
    promoPrice: number | null;
    url: string | null;
}

export interface SnapshotRow {
    productId: number;
    sku: string | null;
    ean: string | null;
    description: string;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
    prices: SnapshotPrice[];
}

export interface CompetitorInfo {
    id: number;
    name: string;
    color: string | null;
}

export interface LatestSnapshotResponse {
    snapshotDate: string | null;
    competitors: CompetitorInfo[];
    rows: SnapshotRow[];
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class PriceSnapshotsService {
    private _http = inject(HttpClient);

    getLatest(take = 200): Observable<LatestSnapshotResponse> {
        const params = new HttpParams().set('take', take);
        return this._http.get<LatestSnapshotResponse>(`${API_BASE}/price-snapshots/latest`, {
            params,
        });
    }

    getByDate(date: string, take = 200): Observable<LatestSnapshotResponse> {
        const params = new HttpParams().set('take', take).set('date', date);
        return this._http.get<LatestSnapshotResponse>(`${API_BASE}/price-snapshots/by-date`, {
            params,
        });
    }
}
