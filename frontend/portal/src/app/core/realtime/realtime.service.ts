import { Injectable, NgZone, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export interface PunchEvent {
  punchId: string;
  employeeId: string;
  employeeCode: string;
  employeeName: string;
  siteId: string;
  timestamp: string;
  direction: 'In' | 'Out';
  source: string;
}

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);
  private readonly zone = inject(NgZone);
  private connection: signalR.HubConnection | null = null;

  readonly connected = signal(false);
  readonly lastPunch = signal<PunchEvent | null>(null);

  async connect(): Promise<void> {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/attendance`, {
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('punchRecorded', (event: PunchEvent) =>
      this.zone.run(() => this.lastPunch.set(event)));

    this.connection.onreconnected(() => this.zone.run(() => this.connected.set(true)));
    this.connection.onclose(() => this.zone.run(() => this.connected.set(false)));

    try {
      await this.connection.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false); // dashboard still works over polling
    }
  }

  async disconnect(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
    this.connected.set(false);
  }
}
