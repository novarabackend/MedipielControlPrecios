import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface ProductItem {
    id: number;
    sku: string;
    ean: string | null;
    description: string;
    brandId: number | null;
    supplierId: number | null;
    categoryId: number | null;
    lineId: number | null;
    medipielListPrice: number | null;
    medipielPromoPrice: number | null;
}

export interface ProductImportItem {
    rowNumber: number;
    sku: string;
    ean?: string | null;
    description?: string | null;
    brand?: string | null;
    supplier?: string | null;
    category?: string | null;
    line?: string | null;
    medipielListPrice?: number | null;
    medipielPromoPrice?: number | null;
}

export interface ImportError {
    rowNumber: number;
    sku?: string | null;
    ean?: string | null;
    message: string;
}

export interface ImportSummary {
    total: number;
    created: number;
    updated: number;
    failed: number;
    errors: ImportError[];
}

const API_BASE = 'http://localhost:5000/api';

@Injectable({ providedIn: 'root' })
export class ProductsService {
    private _http = inject(HttpClient);

    getProducts(): Observable<ProductItem[]> {
        return this._http.get<ProductItem[]>(`${API_BASE}/products`);
    }

    importProducts(items: ProductImportItem[]): Observable<ImportSummary> {
        return this._http.post<ImportSummary>(`${API_BASE}/products/import`, { items });
    }
}
