import { Injectable, NgZone, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
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

  private readonly punches = new Subject<PunchEvent>();

  /**
   * Stream of punches as they happen.
   *
   * Deliberately an Observable rather than a signal: this is a sequence of
   * events, not a piece of state. Exposing it as a signal invites consumers to
   * read it inside an `effect`, and if that effect also writes a signal it
   * reads, the effect re-triggers itself forever — which previously locked up
   * the browser (and the machine) on the first punch.
   */
  readonly punches$: Observable<PunchEvent> = this.punches.asObservable();

  async connect(): Promise<void> {
    if (this.connection) return;

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${environment.apiUrl}/hubs/attendance`, {
        accessTokenFactory: () => this.auth.token() ?? '',
      })
      .withAutomaticReconnect()
      .build();

    this.connection.on('punchRecorded', (event: PunchEvent) =>
      this.zone.run(() => this.punches.next(event)));

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
