import { ScanLine } from "lucide-react";
export function Badge({ value }: { value: string }) {
  return (
    <span className={"badge " + value.toLowerCase()}>
      <i />
      {value}
    </span>
  );
}
export function Empty({ text, detail }: { text: string; detail: string }) {
  return (
    <div className="empty">
      <ScanLine size={30} />
      <strong>{text}</strong>
      <p>{detail}</p>
    </div>
  );
}
