import { Directive, ElementRef } from '@angular/core';
import { firstValueFrom, Subscription } from 'rxjs';
import { AuthService } from '../auth.service';

@Directive({
  selector: '[ifLoggedIn]',
})
export class IfLoggedInDirective {
  susbscription: Subscription | undefined;

  constructor(
    private elementRef: ElementRef<any>,
    private authSvc: AuthService
  ) {}

  ngOnInit(): void {
    this.susbscription = this.authSvc.IsUserLoggedIn.subscribe((loggedIn) => {
      this.elementRef.nativeElement.style.display = loggedIn ? null : 'none';
    });
  }

  ngOnDestroy(): void {
    this.susbscription?.unsubscribe();
  }
}
