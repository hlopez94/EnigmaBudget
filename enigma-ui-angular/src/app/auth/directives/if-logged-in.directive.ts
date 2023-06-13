import { Directive, ElementRef } from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../auth.service';

@Directive({
  selector: '[ifLoggedIn]',
  standalone:true,
  providers:[AuthService]
})
export class IfLoggedInDirective {
  susbscription: Subscription | undefined;

  constructor(
    private elementRef: ElementRef<any>,
    private authSvc: AuthService
  ) {}

  ngOnInit(): void {
    this.susbscription = this.authSvc.$isUserLoggedIn.subscribe((loggedIn) => {
      this.elementRef.nativeElement.style.display = loggedIn ? null : 'none';
    });
  }

  ngOnDestroy(): void {
    this.susbscription?.unsubscribe();
  }
}
