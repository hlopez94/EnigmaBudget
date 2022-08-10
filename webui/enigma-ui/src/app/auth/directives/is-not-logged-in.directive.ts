import { Directive, ElementRef, OnDestroy } from '@angular/core';
import { firstValueFrom, identity, Subscription } from 'rxjs';
import { AuthService } from '../auth.service';

@Directive({
  selector: '[authIsNotLoggedIn]',
})
export class IsNotLoggedInDirective {
  constructor(
    private el: ElementRef<HTMLElement>,
    private authSvc: AuthService
  ) {}

  private async AuthIsNotLogged() {
    var isNotLoggedIn: boolean = await !firstValueFrom(
      this.authSvc.IsUserLoggedIn
    );
    this.el.nativeElement.hidden = isNotLoggedIn;
  }
}
